using System.Globalization;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // SeaweedFS blob store, accessed via its S3-compatible gateway. The instance's Username is
    // the S3 access key and Password the secret key (seeded through AWS_ACCESS_KEY_ID /
    // AWS_SECRET_ACCESS_KEY on the container, which SeaweedFS picks up as an admin identity).
    //
    // Model: buckets = databases, a single virtual "_all_objects" collection per bucket,
    // objects = rows (key / size / last-modified / etag). Deleting a row deletes the object.

    internal static class SeaweedFsS3
    {
        public static AmazonS3Client Build(InnerDatabaseTarget target)
        {
            var config = new AmazonS3Config
            {
                ServiceURL = $"http://{target.Host}:{target.Port}",
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                Timeout = TimeSpan.FromSeconds(10),
                MaxErrorRetry = 0,
                // SDK v4 defaults to always sending CRC checksum headers, which not every
                // S3-compatible store accepts. WHEN_REQUIRED keeps requests plain.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            };
            return new AmazonS3Client(new BasicAWSCredentials(target.Username, target.Password), config);
        }
    }

    public class SeaweedFsInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "seaweedfs";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            using var s3 = SeaweedFsS3.Build(target);
            var resp = await s3.ListBucketsAsync(cancellationToken);
            return (resp.Buckets ?? []).Select(b => b.BucketName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            using var s3 = SeaweedFsS3.Build(target);
            await s3.PutBucketAsync(name, cancellationToken);
        }

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            using var s3 = SeaweedFsS3.Build(target);
            await s3.DeleteBucketAsync(name, cancellationToken);
        }
    }

    public class SeaweedFsInnerUserService : IInnerUserService
    {
        public string Engine => "seaweedfs";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("SeaweedFS identity management is not exposed in this version.");
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("SeaweedFS identity management is not exposed in this version.");
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("SeaweedFS identity management is not exposed in this version.");
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("SeaweedFS identity management is not exposed in this version.");
    }

    public class SeaweedFsInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "seaweedfs";

        private const string VirtualTable = "_all_objects";

        public Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TableSummary>>(new[] { new TableSummary(VirtualTable, "collection") });

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            using var s3 = SeaweedFsS3.Build(target);

            // ponytail: S3 has no offset - page from the start and skip. O(offset) per request;
            // switch to continuation-token caching if anyone browses huge buckets.
            var rows = new List<IReadOnlyList<string?>>();
            var toSkip = offset;
            string? token = null;
            do
            {
                var resp = await s3.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = database,
                    MaxKeys = Math.Min(1000, toSkip + limit - rows.Count),
                    ContinuationToken = token,
                }, cancellationToken);

                foreach (var obj in resp.S3Objects ?? [])
                {
                    if (toSkip > 0) { toSkip--; continue; }
                    if (rows.Count >= limit) break;
                    rows.Add(new[]
                    {
                        obj.Key,
                        obj.Size?.ToString(CultureInfo.InvariantCulture),
                        obj.LastModified?.ToString("o", CultureInfo.InvariantCulture),
                        obj.ETag?.Trim('"'),
                    });
                }
                token = resp.IsTruncated == true ? resp.NextContinuationToken : null;
            } while (token is not null && rows.Count < limit);

            return new TableRows(new[] { "key", "size", "lastModified", "etag" }, rows, null);
        }

        public Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Use object upload to add objects to a bucket.");
        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Objects cannot be edited in place - re-upload with the same key to replace.");

        public async Task UploadObjectAsync(InnerDatabaseTarget target, string database, string key, Stream content, string? contentType, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Object key is required.", nameof(key));
            using var s3 = SeaweedFsS3.Build(target);
            // PutObject overwrites an existing key, so "replace" is just re-upload.
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = database,
                Key = key,
                InputStream = content,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                AutoCloseStream = false,
                // S3-compatible stores generally don't accept aws-chunked streaming uploads - send a
                // single signed payload with Content-Length instead.
                UseChunkEncoding = false,
            }, cancellationToken);
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var keyIndex = -1;
            for (var i = 0; i < request.Columns.Count; i++)
                if (request.Columns[i] == "key") keyIndex = i;
            var key = keyIndex >= 0 ? request.OriginalValues[keyIndex] : null;
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Object key is required to delete.", nameof(request));

            using var s3 = SeaweedFsS3.Build(target);
            await s3.DeleteObjectAsync(database, key, cancellationToken);
        }

        public Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("SeaweedFS exposes a single virtual object listing per bucket.");
    }
}

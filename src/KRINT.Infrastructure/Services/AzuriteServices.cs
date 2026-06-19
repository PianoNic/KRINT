using System.Globalization;
using Azure.Storage.Blobs;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // Azurite - Microsoft's Azure Storage emulator - accessed over the Azure Blob API (Azurite
    // does NOT speak S3, so this can't reuse the SeaweedFS/AWSSDK path). We run it with the
    // built-in development account "devstoreaccount1" and its well-known key.
    //
    // Model mirrors SeaweedFS: containers = databases, a single virtual "_all_blobs" collection
    // per container, blobs = rows (name / size / last-modified / etag). Deleting a row deletes
    // the blob; upload/edit need a file UI - out of scope for v1.

    internal static class AzuriteBlob
    {
        // Public, documented Azurite/Azure dev-storage account + key (not a secret).
        private const string DevAccount = "devstoreaccount1";
        private const string DevKey =
            "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

        public static BlobServiceClient Build(InnerDatabaseTarget target)
        {
            // The instance username is the account name; the dev account uses the fixed well-known
            // key (Azurite ignores the generated password, like cockroachdb's insecure mode).
            var account = string.IsNullOrWhiteSpace(target.Username) ? DevAccount : target.Username;
            var conn =
                $"DefaultEndpointsProtocol=http;AccountName={account};AccountKey={DevKey};" +
                $"BlobEndpoint=http://{target.Host}:{target.Port}/{account};";
            return new BlobServiceClient(conn);
        }
    }

    public class AzuriteInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "azurite";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            // Real call -> also serves as the readiness probe (Azurite/JVM-free but still needs a beat).
            var client = AzuriteBlob.Build(target);
            var names = new List<string>();
            await foreach (var c in client.GetBlobContainersAsync(cancellationToken: cancellationToken))
                names.Add(c.Name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            var client = AzuriteBlob.Build(target);
            await client.CreateBlobContainerAsync(name, cancellationToken: cancellationToken);
        }

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            var client = AzuriteBlob.Build(target);
            await client.DeleteBlobContainerAsync(name, cancellationToken: cancellationToken);
        }
    }

    public class AzuriteInnerUserService : IInnerUserService
    {
        public string Engine => "azurite";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Azurite uses a fixed development account; identity management is not exposed.");
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Azurite uses a fixed development account; identity management is not exposed.");
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Azurite uses a fixed development account; identity management is not exposed.");
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Azurite uses a fixed development account; identity management is not exposed.");
    }

    public class AzuriteInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "azurite";

        private const string VirtualTable = "_all_blobs";

        public Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TableSummary>>(new[] { new TableSummary(VirtualTable, "collection") });

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            var container = AzuriteBlob.Build(target).GetBlobContainerClient(database);

            // Azure Blob listing has no offset - page from the start and skip.
            var rows = new List<IReadOnlyList<string?>>();
            var skipped = 0;
            await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                if (skipped < offset) { skipped++; continue; }
                if (rows.Count >= limit) break;
                rows.Add(new[]
                {
                    blob.Name,
                    blob.Properties.ContentLength?.ToString(CultureInfo.InvariantCulture),
                    blob.Properties.LastModified?.ToString("o", CultureInfo.InvariantCulture),
                    blob.Properties.ETag?.ToString().Trim('"'),
                });
            }

            return new TableRows(new[] { "name", "size", "lastModified", "etag" }, rows, null);
        }

        public Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Use object upload to add blobs to a container.");
        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Blobs cannot be edited in place - re-upload with the same name to replace.");

        public async Task UploadObjectAsync(InnerDatabaseTarget target, string database, string key, Stream content, string? contentType, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Blob name is required.", nameof(key));
            var blob = AzuriteBlob.Build(target).GetBlobContainerClient(database).GetBlobClient(key);
            // overwrite: true so re-uploading the same name replaces the blob.
            await blob.UploadAsync(content, new Azure.Storage.Blobs.Models.BlobUploadOptions
            {
                HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
                {
                    ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                },
            }, cancellationToken);
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var nameIndex = -1;
            for (var i = 0; i < request.Columns.Count; i++)
                if (request.Columns[i] == "name") nameIndex = i;
            var key = nameIndex >= 0 ? request.OriginalValues[nameIndex] : null;
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Blob name is required to delete.", nameof(request));

            var container = AzuriteBlob.Build(target).GetBlobContainerClient(database);
            await container.DeleteBlobAsync(key, cancellationToken: cancellationToken);
        }

        public Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Azurite exposes a single virtual blob listing per container.");
    }
}

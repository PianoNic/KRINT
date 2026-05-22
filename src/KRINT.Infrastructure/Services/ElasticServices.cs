using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // Elasticsearch + OpenSearch both speak the same HTTP+JSON API at this level (the fork
    // diverges on advanced features we don't use here). Shape: a single virtual "_cluster"
    // database; indices are "tables"; docs are "rows" rendered as a JSON document column.
    // Per-doc edit/delete needs the routing _id + _seq_no; out of scope for v1.

    internal static class ElasticHttp
    {
        public static HttpClient Build(InnerDatabaseTarget target, bool insecureTls = false)
        {
            var handler = new HttpClientHandler();
            if (insecureTls)
            {
                // OpenSearch ships with a self-signed cert on first boot. The dev container is
                // pointed at localhost, so we accept its cert without verification.
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }
            var scheme = insecureTls ? "https" : "http";
            var client = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri($"{scheme}://{target.Host}:{target.Port}"),
                Timeout = TimeSpan.FromSeconds(10),
            };
            if (!string.IsNullOrEmpty(target.Username))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{target.Username}:{target.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
            return client;
        }
    }

    public class ElasticInnerDatabaseService : IInnerDatabaseService
    {
        public virtual string Engine => "elasticsearch";
        protected virtual bool UseHttps => false;

        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "_cluster" });

        public Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Elasticsearch/OpenSearch don't have logical databases - there's only the single cluster.");

        public Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Elasticsearch/OpenSearch don't have logical databases.");
    }

    public class ElasticInnerUserService : IInnerUserService
    {
        public virtual string Engine => "elasticsearch";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Elasticsearch/OpenSearch user mgmt is not exposed in this version.");
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Elasticsearch/OpenSearch user mgmt is not exposed in this version.");
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Elasticsearch/OpenSearch user mgmt is not exposed in this version.");
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Elasticsearch/OpenSearch user mgmt is not exposed in this version.");
    }

    public class ElasticInnerSchemaService : IInnerSchemaService
    {
        public virtual string Engine => "elasticsearch";
        protected virtual bool UseHttps => false;

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            using var http = ElasticHttp.Build(target, UseHttps);
            using var resp = await http.GetAsync("/_cat/indices?format=json&h=index", cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var indices = new List<TableSummary>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var name = el.GetProperty("index").GetString();
                if (string.IsNullOrEmpty(name) || name.StartsWith('.')) continue;
                indices.Add(new TableSummary(name, "index"));
            }
            return indices;
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            using var http = ElasticHttp.Build(target, UseHttps);
            var url = $"/{Uri.EscapeDataString(table)}/_search?from={offset}&size={limit}";
            using var resp = await http.GetAsync(url, cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            long? total = null;
            if (doc.RootElement.TryGetProperty("hits", out var hits) &&
                hits.TryGetProperty("total", out var tot) &&
                tot.TryGetProperty("value", out var tv))
            {
                total = tv.GetInt64();
            }

            var rows = new List<IReadOnlyList<string?>>();
            if (hits.ValueKind != JsonValueKind.Undefined && hits.TryGetProperty("hits", out var arr))
            {
                foreach (var h in arr.EnumerateArray())
                {
                    var id = h.GetProperty("_id").GetString();
                    var src = h.TryGetProperty("_source", out var s) ? s.GetRawText() : "{}";
                    rows.Add(new[] { id, src });
                }
            }
            return new TableRows(new[] { "_id", "_source" }, rows, total);
        }

        public Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Elasticsearch/OpenSearch insert is not exposed in this version.");
        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Elasticsearch/OpenSearch document update is not exposed in this version.");
        public Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Elasticsearch/OpenSearch document delete is not exposed in this version.");

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            using var http = ElasticHttp.Build(target, UseHttps);
            using var resp = await http.DeleteAsync($"/{Uri.EscapeDataString(table)}", cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
    }

}

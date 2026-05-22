using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // Couchbase Server over HTTP. We expose: buckets as "databases", collections as "tables",
    // documents as "rows" (rendered as JSON). Browse-only in v1 - N1QL UPDATE/DELETE/INSERT
    // need primary-key knowledge and a query node; user mgmt skipped.
    //
    // Cluster initialisation (POST /clusterInit + bucket create) happens once at provision time;
    // see CreateDatabaseCommand for the Couchbase-specific init path.

    internal static class CouchbaseHttp
    {
        public static HttpClient BuildAdmin(InnerDatabaseTarget target)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri($"http://{target.Host}:{target.Port}"),
                Timeout = TimeSpan.FromSeconds(15),
            };
            if (!string.IsNullOrEmpty(target.Username))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{target.Username}:{target.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
            return client;
        }
    }

    public class CouchbaseInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "couchbase";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            using var http = CouchbaseHttp.BuildAdmin(target);
            using var resp = await http.GetAsync("/pools/default/buckets", cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var name = el.GetProperty("name").GetString();
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            return result;
        }

        public Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Couchbase bucket creation is not exposed in this version - sizing matters and warrants its own UI.");
        public Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Couchbase bucket deletion is not exposed in this version.");
    }

    public class CouchbaseInnerUserService : IInnerUserService
    {
        public string Engine => "couchbase";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public class CouchbaseInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "couchbase";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            using var http = CouchbaseHttp.BuildAdmin(target);
            // Scopes/collections under a bucket. The default scope ("_default") is the one that ships pre-created.
            using var resp = await http.GetAsync($"/pools/default/buckets/{Uri.EscapeDataString(database)}/scopes", cancellationToken);
            if (!resp.IsSuccessStatusCode) return Array.Empty<TableSummary>();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = new List<TableSummary>();
            if (doc.RootElement.TryGetProperty("scopes", out var scopes))
                foreach (var scope in scopes.EnumerateArray())
                    if (scope.TryGetProperty("collections", out var cols))
                        foreach (var col in cols.EnumerateArray())
                        {
                            var name = col.GetProperty("name").GetString();
                            if (!string.IsNullOrEmpty(name)) result.Add(new TableSummary(name, "collection"));
                        }
            return result;
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 500);
            using var http = CouchbaseHttp.BuildAdmin(target);
            // N1QL via the query service on the same admin port? No - query lives on 8093.
            // Hosted target only publishes 8091; query/collection rows skipped for v1.
            return new TableRows(new[] { "_id", "document" }, Array.Empty<IReadOnlyList<string?>>(), null);
        }

        public Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            // Drop a collection within the default scope.
            using var http = CouchbaseHttp.BuildAdmin(target);
            using var resp = await http.DeleteAsync(
                $"/pools/default/buckets/{Uri.EscapeDataString(database)}/scopes/_default/collections/{Uri.EscapeDataString(table)}",
                cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
    }
}

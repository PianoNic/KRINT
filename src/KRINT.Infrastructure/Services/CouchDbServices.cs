using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // CouchDB is a JSON-over-HTTP document store. We talk to it with HttpClient directly - no
    // SDK needed. Map: "database" = real CouchDB database, "table" = single virtual "_all_docs"
    // namespace per database (since docs live directly inside a DB), "row" = one document.
    // Per-doc edit/delete requires the _rev hash; we surface read-only browsing for v1.

    internal static class CouchDbHttp
    {
        public static HttpClient Build(InnerDatabaseTarget target)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri($"http://{target.Host}:{target.Port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };
            if (!string.IsNullOrEmpty(target.Username))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{target.Username}:{target.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
            return client;
        }
    }

    public class CouchDbInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "couchdb";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            using var http = CouchDbHttp.Build(target);
            var names = await http.GetFromJsonAsync<string[]>("/_all_dbs", cancellationToken) ?? Array.Empty<string>();
            // Hide CouchDB's internal _users / _replicator / _global_changes etc.
            return names.Where(n => !n.StartsWith('_')).ToList();
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            using var http = CouchDbHttp.Build(target);
            var resp = await http.PutAsync($"/{Uri.EscapeDataString(name)}", null, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (string.Equals(name, target.DefaultDatabase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to drop the instance's default database '{name}'.");
            using var http = CouchDbHttp.Build(target);
            var resp = await http.DeleteAsync($"/{Uri.EscapeDataString(name)}", cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
    }

    public class CouchDbInnerUserService : IInnerUserService
    {
        public string Engine => "couchdb";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("CouchDB user mgmt is not exposed in this version.");
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("CouchDB user mgmt is not exposed in this version.");
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("CouchDB user mgmt is not exposed in this version.");
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("CouchDB user mgmt is not exposed in this version.");
    }

    public class CouchDbInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "couchdb";
        private const string DocsCollection = "_all_docs";

        public Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            IReadOnlyList<TableSummary> result = new[] { new TableSummary(DocsCollection, "collection") };
            return Task.FromResult(result);
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(table, DocsCollection, StringComparison.Ordinal))
                throw new ArgumentException($"CouchDB exposes only the virtual collection '{DocsCollection}'.");
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            using var http = CouchDbHttp.Build(target);
            var url = $"/{Uri.EscapeDataString(database)}/_all_docs?include_docs=true&limit={limit}&skip={offset}";
            using var resp = await http.GetAsync(url, cancellationToken);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            long? total = null;
            if (doc.RootElement.TryGetProperty("total_rows", out var t)) total = t.GetInt64();

            var rows = new List<IReadOnlyList<string?>>();
            if (doc.RootElement.TryGetProperty("rows", out var rowsArr))
            {
                foreach (var row in rowsArr.EnumerateArray())
                {
                    var id = row.GetProperty("id").GetString();
                    var json = row.TryGetProperty("doc", out var d) ? d.GetRawText() : row.GetRawText();
                    rows.Add(new[] { id, json });
                }
            }
            return new TableRows(new[] { "_id", "document" }, rows, total);
        }

        private static int Idx(IReadOnlyList<string> cols, string name)
        {
            for (var i = 0; i < cols.Count; i++) if (cols[i] == name) return i;
            return -1;
        }

        public async Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var docJson = request.Values[Idx(request.Columns, "document")] ?? "{}";
            var node = JsonNode.Parse(docJson)!.AsObject();
            node.Remove("_rev"); // a new doc must not carry a rev
            using var http = CouchDbHttp.Build(target);
            var content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
            // POST to the database: CouchDB honours an embedded _id, or assigns one when absent.
            using var resp = await http.PostAsync($"/{Uri.EscapeDataString(database)}", content, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        public async Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
        {
            var idIdx = Idx(request.Columns, "_id");
            var docIdx = Idx(request.Columns, "document");
            var id = request.OriginalValues[idIdx];
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Document _id is required to update.");
            // CouchDB needs the current _rev; take it from the original doc and stamp it on the new body.
            var rev = JsonDocument.Parse(request.OriginalValues[docIdx] ?? "{}").RootElement.TryGetProperty("_rev", out var r) ? r.GetString() : null;
            var node = JsonNode.Parse(request.NewValues[docIdx] ?? "{}")!.AsObject();
            node["_id"] = id;
            if (rev is not null) node["_rev"] = rev;
            using var http = CouchDbHttp.Build(target);
            var content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
            using var resp = await http.PutAsync($"/{Uri.EscapeDataString(database)}/{Uri.EscapeDataString(id)}", content, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            var idIdx = Idx(request.Columns, "_id");
            var docIdx = Idx(request.Columns, "document");
            var id = request.OriginalValues[idIdx];
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Document _id is required to delete.");
            var rev = JsonDocument.Parse(request.OriginalValues[docIdx] ?? "{}").RootElement.TryGetProperty("_rev", out var r) ? r.GetString() : null;
            if (string.IsNullOrWhiteSpace(rev)) throw new ArgumentException("Document _rev is required to delete.");
            using var http = CouchDbHttp.Build(target);
            using var resp = await http.DeleteAsync($"/{Uri.EscapeDataString(database)}/{Uri.EscapeDataString(id)}?rev={Uri.EscapeDataString(rev)}", cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
        public Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("CouchDB has no tables - drop the database instead.");
    }
}

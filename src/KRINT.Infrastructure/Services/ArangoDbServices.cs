using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // ArangoDB multi-model. HTTP+JSON API. Map: database → ArangoDB database, table → collection,
    // row → document. Per-doc edit/delete needs _key + _rev so caps say no for v1.

    internal static class ArangoHttp
    {
        public static HttpClient Build(InnerDatabaseTarget target, string? dbName = null)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri($"http://{target.Host}:{target.Port}"),
                Timeout = TimeSpan.FromSeconds(10),
            };
            if (!string.IsNullOrEmpty(target.Username))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{target.Username}:{target.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
            return client;
        }

        public static string DbPrefix(string database) => $"/_db/{Uri.EscapeDataString(database)}";
    }

    public class ArangoDbInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "arangodb";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            using var http = ArangoHttp.Build(target);
            using var resp = await http.GetAsync("/_api/database", cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = new List<string>();
            foreach (var el in doc.RootElement.GetProperty("result").EnumerateArray())
            {
                var name = el.GetString();
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            return result;
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            using var http = ArangoHttp.Build(target);
            using var resp = await http.PostAsJsonAsync("/_api/database", new { name }, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (string.Equals(name, target.DefaultDatabase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to drop the instance's default database '{name}'.");
            using var http = ArangoHttp.Build(target);
            using var resp = await http.DeleteAsync($"/_api/database/{Uri.EscapeDataString(name)}", cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
    }

    public class ArangoDbInnerUserService : IInnerUserService
    {
        public string Engine => "arangodb";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("ArangoDB user mgmt is not exposed in this version.");
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("ArangoDB user mgmt is not exposed in this version.");
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("ArangoDB user mgmt is not exposed in this version.");
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("ArangoDB user mgmt is not exposed in this version.");
    }

    public class ArangoDbInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "arangodb";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            using var http = ArangoHttp.Build(target);
            using var resp = await http.GetAsync($"{ArangoHttp.DbPrefix(database)}/_api/collection?excludeSystem=true", cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = new List<TableSummary>();
            foreach (var el in doc.RootElement.GetProperty("result").EnumerateArray())
            {
                var name = el.GetProperty("name").GetString();
                if (!string.IsNullOrEmpty(name)) result.Add(new TableSummary(name, "collection"));
            }
            return result;
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            using var http = ArangoHttp.Build(target);

            // Count first (cheap on a collection with a count index).
            long? total = null;
            using (var cntResp = await http.GetAsync($"{ArangoHttp.DbPrefix(database)}/_api/collection/{Uri.EscapeDataString(table)}/count", cancellationToken))
            {
                if (cntResp.IsSuccessStatusCode)
                {
                    await using var s = await cntResp.Content.ReadAsStreamAsync(cancellationToken);
                    using var cnt = await JsonDocument.ParseAsync(s, cancellationToken: cancellationToken);
                    if (cnt.RootElement.TryGetProperty("count", out var c)) total = c.GetInt64();
                }
            }

            // AQL: page through documents.
            var body = new
            {
                query = "FOR doc IN @@col LIMIT @offset, @limit RETURN doc",
                bindVars = new Dictionary<string, object> { ["@col"] = table, ["offset"] = offset, ["limit"] = limit },
            };
            using var resp = await http.PostAsJsonAsync($"{ArangoHttp.DbPrefix(database)}/_api/cursor", body, cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var rows = new List<IReadOnlyList<string?>>();
            foreach (var el in doc.RootElement.GetProperty("result").EnumerateArray())
            {
                string? key = el.TryGetProperty("_key", out var k) ? k.GetString() : null;
                rows.Add(new[] { key, el.GetRawText() });
            }
            return new TableRows(new[] { "_key", "document" }, rows, total);
        }

        public async Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.Values.Count)
                throw new ArgumentException("Columns and values must have the same non-zero length.", nameof(request));

            // The UI sends columns + values; treat the "document" column as a raw JSON body if present,
            // otherwise build a flat object from the other columns.
            string body;
            var docIdx = -1;
            for (var i = 0; i < request.Columns.Count; i++)
                if (string.Equals(request.Columns[i], "document", StringComparison.OrdinalIgnoreCase)) { docIdx = i; break; }

            if (docIdx >= 0)
            {
                body = request.Values[docIdx] ?? "{}";
            }
            else
            {
                var sb = new StringBuilder();
                sb.Append('{');
                for (var i = 0; i < request.Columns.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(JsonSerializer.Serialize(request.Columns[i]));
                    sb.Append(':');
                    sb.Append(JsonSerializer.Serialize(request.Values[i]));
                }
                sb.Append('}');
                body = sb.ToString();
            }

            using var http = ArangoHttp.Build(target);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(
                $"{ArangoHttp.DbPrefix(database)}/_api/document/{Uri.EscapeDataString(table)}",
                content, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("ArangoDB document update not exposed in this version (needs _key + _rev).");
        public Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("ArangoDB document delete not exposed in this version (needs _key + _rev).");

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            using var http = ArangoHttp.Build(target);
            using var resp = await http.DeleteAsync($"{ArangoHttp.DbPrefix(database)}/_api/collection/{Uri.EscapeDataString(table)}", cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
    }
}

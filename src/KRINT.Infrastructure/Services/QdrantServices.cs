using System.Net.Http.Headers;
using System.Text.Json;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // Qdrant vector DB. Read-only browse of collections + their points. Auth via api-key header
    // (QDRANT__SERVICE__API_KEY env, passed as our "password"). HTTP REST.
    //
    // Model: single virtual "cluster" database, collections = tables, points = rows.
    // Per-point edit/insert/delete need vector payloads, which is out of scope for v1.

    internal static class QdrantHttp
    {
        public static HttpClient Build(InnerDatabaseTarget target)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri($"http://{target.Host}:{target.Port}"),
                Timeout = TimeSpan.FromSeconds(10),
            };
            if (!string.IsNullOrEmpty(target.Password))
                client.DefaultRequestHeaders.Add("api-key", target.Password);
            return client;
        }
    }

    public class QdrantInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "qdrant";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "_cluster" });
        public Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Qdrant has no logical databases.");
        public Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Qdrant has no logical databases.");
    }

    public class QdrantInnerUserService : IInnerUserService
    {
        public string Engine => "qdrant";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Qdrant user mgmt is not exposed in this version.");
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Qdrant user mgmt is not exposed in this version.");
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Qdrant user mgmt is not exposed in this version.");
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Qdrant user mgmt is not exposed in this version.");
    }

    public class QdrantInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "qdrant";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            using var http = QdrantHttp.Build(target);
            using var resp = await http.GetAsync("/collections", cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = new List<TableSummary>();
            if (doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("collections", out var cols))
            {
                foreach (var col in cols.EnumerateArray())
                {
                    var name = col.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(name)) result.Add(new TableSummary(name, "collection"));
                }
            }
            return result;
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 500);
            using var http = QdrantHttp.Build(target);

            // /collections/{name}/points/scroll
            var body = new { limit, with_payload = true, with_vector = false, offset = (object?)null };
            using var resp = await http.PostAsync($"/collections/{Uri.EscapeDataString(table)}/points/scroll", new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"), cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            // Get a collection count for the totals row.
            long? total = null;
            using (var cntResp = await http.PostAsync( $"/collections/{Uri.EscapeDataString(table)}/points/count", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), cancellationToken))
            {
                if (cntResp.IsSuccessStatusCode)
                {
                    await using var s = await cntResp.Content.ReadAsStreamAsync(cancellationToken);
                    using var cnt = await JsonDocument.ParseAsync(s, cancellationToken: cancellationToken);
                    if (cnt.RootElement.TryGetProperty("result", out var rr) && rr.TryGetProperty("count", out var c))
                        total = c.GetInt64();
                }
            }

            var rows = new List<IReadOnlyList<string?>>();
            if (doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("points", out var pts))
            {
                foreach (var p in pts.EnumerateArray())
                {
                    var id = p.TryGetProperty("id", out var i) ? i.ToString() : null;
                    var payload = p.TryGetProperty("payload", out var pl) ? pl.GetRawText() : "{}";
                    rows.Add(new[] { id, payload });
                }
            }
            return new TableRows(new[] { "id", "payload" }, rows, total);
        }

        public Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Qdrant point insert needs a vector - not exposed in this version.");

        private static int Idx(IReadOnlyList<string> cols, string name)
        {
            for (var i = 0; i < cols.Count; i++) if (cols[i] == name) return i;
            return -1;
        }

        // Point ids are either integers or UUID strings; emit the correct JSON token for each.
        private static string IdToken(string? id) => long.TryParse(id, out _) ? id! : JsonSerializer.Serialize(id ?? "");

        public async Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
        {
            var id = request.OriginalValues[Idx(request.Columns, "id")];
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Point id is required to update.");
            var payload = request.NewValues[Idx(request.Columns, "payload")] ?? "{}";
            using var http = QdrantHttp.Build(target);
            // Overwrite the point's payload (vector is left untouched).
            var body = $"{{\"points\":[{IdToken(id)}],\"payload\":{payload}}}";
            using var resp = await http.PostAsync($"/collections/{Uri.EscapeDataString(table)}/points/payload?wait=true", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            var id = request.OriginalValues[Idx(request.Columns, "id")];
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Point id is required to delete.");
            using var http = QdrantHttp.Build(target);
            var body = $"{{\"points\":[{IdToken(id)}]}}";
            using var resp = await http.PostAsync($"/collections/{Uri.EscapeDataString(table)}/points/delete?wait=true", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            using var http = QdrantHttp.Build(target);
            using var resp = await http.DeleteAsync($"/collections/{Uri.EscapeDataString(table)}", cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
    }
}

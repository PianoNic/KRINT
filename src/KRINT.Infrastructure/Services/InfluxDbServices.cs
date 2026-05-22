using System.Net.Http.Headers;
using System.Text.Json;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // InfluxDB v2 over HTTP. Read-only browse of orgs (databases) and buckets (tables).
    // Points (rows) are queried via Flux; we return raw line-protocol-ish rows when there
    // are any. Per-point CRUD is out of scope - Influx mutations are deliberate and rare.

    internal static class InfluxHttp
    {
        public static HttpClient Build(InnerDatabaseTarget target)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri($"http://{target.Host}:{target.Port}"),
                Timeout = TimeSpan.FromSeconds(10),
            };
            // We use the password slot for the API token to keep the secrets-vault contract uniform.
            if (!string.IsNullOrEmpty(target.Password))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", target.Password);
            return client;
        }
    }

    public class InfluxDbInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "influxdb";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            using var http = InfluxHttp.Build(target);
            using var resp = await http.GetAsync("/api/v2/orgs", cancellationToken);
            if (!resp.IsSuccessStatusCode) return new[] { target.DefaultDatabase };
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = new List<string>();
            if (doc.RootElement.TryGetProperty("orgs", out var orgs))
                foreach (var o in orgs.EnumerateArray())
                    if (o.TryGetProperty("name", out var n) && n.GetString() is string s) result.Add(s);
            return result.Count > 0 ? result : new[] { target.DefaultDatabase };
        }

        public Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("InfluxDB org creation is not exposed in this version.");
        public Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("InfluxDB org deletion is not exposed in this version.");
    }

    public class InfluxDbInnerUserService : IInnerUserService
    {
        public string Engine => "influxdb";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public class InfluxDbInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "influxdb";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            using var http = InfluxHttp.Build(target);
            // List buckets for the org. Filtering would require an orgID lookup; this returns all visible buckets.
            using var resp = await http.GetAsync("/api/v2/buckets", cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = new List<TableSummary>();
            if (doc.RootElement.TryGetProperty("buckets", out var buckets))
                foreach (var b in buckets.EnumerateArray())
                {
                    var name = b.GetProperty("name").GetString();
                    if (string.IsNullOrEmpty(name) || name.StartsWith('_')) continue;
                    result.Add(new TableSummary(name, "bucket"));
                }
            return result;
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 500);
            using var http = InfluxHttp.Build(target);
            var flux = $"from(bucket:\"{table.Replace("\"", "\\\"")}\") |> range(start: -30d) |> limit(n: {limit})";
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/query?org={Uri.EscapeDataString(database)}")
            {
                Content = new StringContent($"{{\"query\":\"{flux}\",\"type\":\"flux\"}}", System.Text.Encoding.UTF8, "application/json"),
            };
            req.Headers.Accept.ParseAdd("application/csv");
            using var resp = await http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) return new TableRows(new[] { "result", "table", "_time", "_measurement", "_field", "_value" }, Array.Empty<IReadOnlyList<string?>>(), null);

            var csv = await resp.Content.ReadAsStringAsync(cancellationToken);
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')).ToList();
            if (lines.Count == 0) return new TableRows(Array.Empty<string>(), Array.Empty<IReadOnlyList<string?>>(), null);

            var headers = lines[0].Split(',');
            var rows = lines.Skip(1).Select(l => (IReadOnlyList<string?>)l.Split(',').Select(s => (string?)s).ToArray()).ToList();
            return new TableRows(headers, rows, null);
        }

        public Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default) => throw new NotSupportedException("Influx bucket deletion not exposed in this version.");
    }
}

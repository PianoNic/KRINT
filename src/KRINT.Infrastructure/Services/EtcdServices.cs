using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // etcd v3 HTTP/JSON gateway. Endpoints are POST-only with base64-encoded keys and values:
    //   /v3/kv/range   - scan / get
    //   /v3/kv/put     - set
    //   /v3/kv/deleterange - delete
    // We expose a single virtual database "default" and a single virtual table "keys" so the
    // browse contract holds. Auth is off by default in our provisioned container.

    internal static class EtcdHttp
    {
        public static HttpClient Build(InnerDatabaseTarget target)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri($"http://{target.Host}:{target.Port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };
            return client;
        }

        public static string ToB64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        public static string FromB64(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));

        // For a "list everything" scan we send key="\0" and range_end="\0" - etcd interprets a
        // zero-byte range_end as "everything".
        public const string ZeroByte = "AA=="; // base64("\0")
    }

    public class EtcdInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "etcd";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "default" });
        public Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("etcd has no logical databases.");
        public Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("etcd has no logical databases.");
    }

    public class EtcdInnerUserService : IInnerUserService
    {
        public string Engine => "etcd";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("etcd user mgmt is not exposed in this version.");
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("etcd user mgmt is not exposed in this version.");
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("etcd user mgmt is not exposed in this version.");
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("etcd user mgmt is not exposed in this version.");
    }

    public class EtcdInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "etcd";
        private const string KeysTable = "keys";

        public Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TableSummary>>(new[] { new TableSummary(KeysTable, "keyspace") });

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(table, KeysTable, StringComparison.Ordinal))
                throw new ArgumentException($"etcd exposes only the virtual table '{KeysTable}'.");
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            using var http = EtcdHttp.Build(target);
            // Scan everything. Use sort_order=ASCEND and limit=offset+limit; slice locally.
            var body = new
            {
                key = EtcdHttp.ZeroByte,
                range_end = EtcdHttp.ZeroByte,
                limit = offset + limit,
                sort_order = 1,  // ASCEND
                sort_target = 0, // KEY
            };
            using var resp = await http.PostAsJsonAsync("/v3/kv/range", body, cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            long? total = null;
            if (doc.RootElement.TryGetProperty("count", out var c) && long.TryParse(c.GetString(), out var cn)) total = cn;

            var rows = new List<IReadOnlyList<string?>>();
            if (doc.RootElement.TryGetProperty("kvs", out var kvs))
            {
                var i = 0;
                foreach (var kv in kvs.EnumerateArray())
                {
                    if (i++ < offset) continue;
                    if (rows.Count >= limit) break;
                    var key = kv.TryGetProperty("key", out var k) ? EtcdHttp.FromB64(k.GetString() ?? "") : null;
                    var value = kv.TryGetProperty("value", out var v) ? EtcdHttp.FromB64(v.GetString() ?? "") : null;
                    rows.Add(new[] { key, value });
                }
            }
            return new TableRows(new[] { "key", "value" }, rows, total);
        }

        public async Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
        {
            var (key, value) = ExtractKeyValue(request.Columns, request.Values);
            using var http = EtcdHttp.Build(target);
            using var resp = await http.PostAsJsonAsync("/v3/kv/put", new { key = EtcdHttp.ToB64(key), value = EtcdHttp.ToB64(value ?? "") }, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        public async Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
        {
            // PUT is upsert in etcd - same call as Insert.
            var (key, newValue) = ExtractKeyValue(request.Columns, request.NewValues);
            using var http = EtcdHttp.Build(target);
            using var resp = await http.PostAsJsonAsync("/v3/kv/put", new { key = EtcdHttp.ToB64(key), value = EtcdHttp.ToB64(newValue ?? "") }, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            var key = FindColumn(request.Columns, request.OriginalValues, "key")
                ?? throw new ArgumentException("Missing 'key' column.");
            using var http = EtcdHttp.Build(target);
            using var resp = await http.PostAsJsonAsync("/v3/kv/deleterange", new { key = EtcdHttp.ToB64(key) }, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }

        public Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("etcd has no tables to drop.");

        private static (string Key, string? Value) ExtractKeyValue(IReadOnlyList<string> columns, IReadOnlyList<string?> values)
        {
            var key = FindColumn(columns, values, "key") ?? throw new ArgumentException("Missing 'key' column.");
            var value = FindColumn(columns, values, "value");
            return (key, value);
        }

        private static string? FindColumn(IReadOnlyList<string> columns, IReadOnlyList<string?> values, string name)
        {
            for (var i = 0; i < columns.Count; i++)
                if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase)) return values[i];
            return null;
        }
    }
}

using System.Net.Http.Headers;
using System.Text.Json;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // Meilisearch over HTTP. Single virtual "_cluster" database, indexes = tables, docs = rows.
    // Auth via master-key bearer token (passed as our "password").

    internal static class MeiliHttp
    {
        public static HttpClient Build(InnerDatabaseTarget target)
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri($"http://{target.Host}:{target.Port}"),
                Timeout = TimeSpan.FromSeconds(10),
            };
            if (!string.IsNullOrEmpty(target.Password))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", target.Password);
            return client;
        }
    }

    public class MeilisearchInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "meilisearch";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "_cluster" });
        public Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Meilisearch has no logical databases.");
        public Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Meilisearch has no logical databases.");
    }

    public class MeilisearchInnerUserService : IInnerUserService
    {
        public string Engine => "meilisearch";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public class MeilisearchInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "meilisearch";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            using var http = MeiliHttp.Build(target);
            using var resp = await http.GetAsync("/indexes?limit=1000", cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = new List<TableSummary>();
            if (doc.RootElement.TryGetProperty("results", out var arr))
                foreach (var el in arr.EnumerateArray())
                {
                    var uid = el.GetProperty("uid").GetString();
                    if (!string.IsNullOrEmpty(uid)) result.Add(new TableSummary(uid, "index"));
                }
            return result;
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 500);
            using var http = MeiliHttp.Build(target);
            using var resp = await http.GetAsync($"/indexes/{Uri.EscapeDataString(table)}/documents?limit={limit}&offset={offset}", cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            long? total = doc.RootElement.TryGetProperty("total", out var t) ? t.GetInt64() : null;
            var rows = new List<IReadOnlyList<string?>>();
            if (doc.RootElement.TryGetProperty("results", out var arr))
                foreach (var el in arr.EnumerateArray())
                    rows.Add(new[] { (string?)el.GetRawText() });
            return new TableRows(new[] { "document" }, rows, total);
        }

        public Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            using var http = MeiliHttp.Build(target);
            using var resp = await http.DeleteAsync($"/indexes/{Uri.EscapeDataString(table)}", cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
    }
}

using System.Text.Json;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // Apache Solr (standalone mode). Single virtual "_cluster" database, cores = tables,
    // documents = rows. We use the HTTP REST endpoints; auth is off by default.

    internal static class SolrHttp
    {
        public static HttpClient Build(InnerDatabaseTarget target)
        {
            return new HttpClient
            {
                BaseAddress = new Uri($"http://{target.Host}:{target.Port}"),
                Timeout = TimeSpan.FromSeconds(10),
            };
        }
    }

    public class SolrInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "solr";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "_cluster" });
        public Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Solr has no logical databases.");
        public Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Solr has no logical databases.");
    }

    public class SolrInnerUserService : IInnerUserService
    {
        public string Engine => "solr";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public class SolrInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "solr";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            using var http = SolrHttp.Build(target);
            using var resp = await http.GetAsync("/solr/admin/cores?action=STATUS&wt=json", cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = new List<TableSummary>();
            if (doc.RootElement.TryGetProperty("status", out var status))
                foreach (var prop in status.EnumerateObject())
                    result.Add(new TableSummary(prop.Name, "core"));
            return result;
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 500);
            using var http = SolrHttp.Build(target);
            var url = $"/solr/{Uri.EscapeDataString(table)}/select?q=*:*&rows={limit}&start={offset}&wt=json";
            using var resp = await http.GetAsync(url, cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            long? total = null;
            if (doc.RootElement.TryGetProperty("response", out var r))
            {
                if (r.TryGetProperty("numFound", out var nf)) total = nf.GetInt64();
                var rows = new List<IReadOnlyList<string?>>();
                if (r.TryGetProperty("docs", out var docs))
                    foreach (var d in docs.EnumerateArray())
                    {
                        var id = d.TryGetProperty("id", out var i) ? i.ToString() : null;
                        rows.Add(new[] { id, d.GetRawText() });
                    }
                return new TableRows(new[] { "id", "document" }, rows, total);
            }
            return new TableRows(new[] { "id", "document" }, Array.Empty<IReadOnlyList<string?>>(), null);
        }

        public Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            using var http = SolrHttp.Build(target);
            using var resp = await http.GetAsync($"/solr/admin/cores?action=UNLOAD&core={Uri.EscapeDataString(table)}&deleteIndex=true", cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
    }
}

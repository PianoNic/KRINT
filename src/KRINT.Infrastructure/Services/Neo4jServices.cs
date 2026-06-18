using System.Text.Json;
using KRINT.Infrastructure.Interfaces;
using Neo4j.Driver;

namespace KRINT.Infrastructure.Services
{
    // Neo4j over the Bolt protocol. Our model: "database" = a Neo4j database (single one on
    // Community), "table" = node label, "row" = node. Read-only browse; edit/delete need
    // node-id knowledge and tend to be transactional in awkward ways for v1.

    internal static class Neo4jConnect
    {
        public static IDriver Build(InnerDatabaseTarget target)
        {
            var uri = new Uri($"bolt://{target.Host}:{target.Port}");
            return GraphDatabase.Driver(uri, AuthTokens.Basic(target.Username, target.Password));
        }
    }

    public class Neo4jInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "neo4j";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            using var driver = Neo4jConnect.Build(target);
            await using var session = driver.AsyncSession();
            // SHOW DATABASES is enterprise-only; Community has just "neo4j" + "system".
            try
            {
                var result = await session.RunAsync("SHOW DATABASES YIELD name RETURN name");
                var records = await result.ToListAsync(cancellationToken);
                return records.Select(r => r["name"].As<string>()).Where(n => !string.Equals(n, "system", StringComparison.OrdinalIgnoreCase)).ToList();
            }
            catch (Neo4j.Driver.ClientException)
            {
                // SHOW DATABASES is Enterprise-only; on Community it's a query-level ClientException,
                // so fall back to the single "neo4j" db. Connection errors (ServiceUnavailable) are
                // deliberately NOT caught - they must propagate so the readiness probe keeps waiting
                // instead of treating a still-booting server as ready.
                return new[] { "neo4j" };
            }
        }

        public Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Neo4j Community runs as a single database - multi-database is an Enterprise feature.");
        public Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Neo4j Community runs as a single database - multi-database is an Enterprise feature.");
    }

    public class Neo4jInnerUserService : IInnerUserService
    {
        public string Engine => "neo4j";
        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Neo4j user mgmt is not exposed in this version.");
        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Neo4j user mgmt is not exposed in this version.");
        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Neo4j user mgmt is not exposed in this version.");
        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Neo4j user mgmt is not exposed in this version.");
    }

    public class Neo4jInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "neo4j";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            using var driver = Neo4jConnect.Build(target);
            await using var session = driver.AsyncSession(o => o.WithDatabase(database));
            var result = await session.RunAsync("CALL db.labels() YIELD label RETURN label ORDER BY label");
            var records = await result.ToListAsync(cancellationToken);
            return records.Select(r => new TableSummary(r["label"].As<string>(), "label")).ToList();
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);
            using var driver = Neo4jConnect.Build(target);
            await using var session = driver.AsyncSession(o => o.WithDatabase(database));

            long? total = null;
            var countResult = await session.RunAsync($"MATCH (n:`{table}`) RETURN count(n) AS c");
            var countRecord = await countResult.SingleAsync(cancellationToken);
            total = countRecord["c"].As<long>();

            var result = await session.RunAsync($"MATCH (n:`{table}`) RETURN elementId(n) AS id, n SKIP $skip LIMIT $limit",
                new { skip = (long)offset, limit = (long)limit });
            var records = await result.ToListAsync(cancellationToken);

            var rows = new List<IReadOnlyList<string?>>();
            foreach (var rec in records)
            {
                var id = rec["id"].As<string>();
                var node = rec["n"].As<INode>();
                var json = JsonSerializer.Serialize(node.Properties);
                rows.Add(new[] { (string?)id, (string?)json });
            }
            return new TableRows(new[] { "_id", "properties" }, rows, total);
        }

        private static int Idx(IReadOnlyList<string> cols, string name)
        {
            for (var i = 0; i < cols.Count; i++) if (cols[i] == name) return i;
            return -1;
        }

        // Neo4j node properties must be primitives/arrays - convert the JSON object to a CLR map.
        private static Dictionary<string, object?> PropsFromJson(string? json)
        {
            var map = new Dictionary<string, object?>();
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                map[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.TryGetInt64(out var l) ? l : p.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => p.Value.GetRawText(),
                };
            }
            return map;
        }

        public async Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
        {
            var props = PropsFromJson(request.Values[Idx(request.Columns, "properties")]);
            using var driver = Neo4jConnect.Build(target);
            await using var session = driver.AsyncSession(o => o.WithDatabase(database));
            await session.RunAsync($"CREATE (n:`{table}`) SET n = $props", new { props });
        }

        public async Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
        {
            var id = request.OriginalValues[Idx(request.Columns, "_id")];
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Node elementId is required to update.");
            var props = PropsFromJson(request.NewValues[Idx(request.Columns, "properties")]);
            using var driver = Neo4jConnect.Build(target);
            await using var session = driver.AsyncSession(o => o.WithDatabase(database));
            await session.RunAsync("MATCH (n) WHERE elementId(n) = $id SET n = $props", new { id, props });
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            var id = request.OriginalValues[Idx(request.Columns, "_id")];
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Node elementId is required to delete.");
            using var driver = Neo4jConnect.Build(target);
            await using var session = driver.AsyncSession(o => o.WithDatabase(database));
            await session.RunAsync("MATCH (n) WHERE elementId(n) = $id DETACH DELETE n", new { id });
        }

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            // Drop = remove the label from every matching node. The label itself "vanishes" once no nodes carry it.
            using var driver = Neo4jConnect.Build(target);
            await using var session = driver.AsyncSession(o => o.WithDatabase(database));
            await session.RunAsync($"MATCH (n:`{table}`) DETACH DELETE n");
        }
    }
}

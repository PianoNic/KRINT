using System.Globalization;
using Cassandra;
using KRINT.Infrastructure.Interfaces;
using ISession = Cassandra.ISession;

namespace KRINT.Infrastructure.Services
{
    // Cassandra + ScyllaDB share the CQL binary protocol and the same DataStax driver. We
    // provision with auth disabled for v1 because cassandra's auth bootstrap (PasswordAuthenticator
    // in cassandra.yaml + role propagation) is non-trivial to set on first boot. Caps reflect
    // this: SupportsUsers=false, SupportsRowEdit/Delete=false (UPDATE/DELETE without the full
    // primary key is unsafe), SupportsBackup=false (nodetool snapshot is its own thing).

    internal static class CassandraConnect
    {
        public static ICluster Build(InnerDatabaseTarget target)
        {
            var builder = Cluster.Builder()
                .AddContactPoint(target.Host)
                .WithPort(target.Port)
                .WithDefaultKeyspace(null);
            if (!string.IsNullOrEmpty(target.Username) && !string.IsNullOrEmpty(target.Password))
                builder = builder.WithCredentials(target.Username, target.Password);
            builder = builder.WithSocketOptions(new SocketOptions().SetConnectTimeoutMillis(5000));
            return builder.Build();
        }
    }

    public class CassandraInnerDatabaseService : IInnerDatabaseService
    {
        public virtual string Engine => "cassandra";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            // ScyllaDB rejects the multi-value NOT IN list inline (no viable alternative parser),
            // so we list all keyspaces and filter the system ones out in C#. Compatible with both.
            var system = new HashSet<string>(StringComparer.Ordinal)
            {
                "system","system_schema","system_auth","system_traces","system_distributed",
                "system_virtual_schema","system_views","system_replicated_keyspaces",
            };
            using var cluster = CassandraConnect.Build(target);
            using var session = await cluster.ConnectAsync();
            var rs = await session.ExecuteAsync(new SimpleStatement(
                "SELECT keyspace_name FROM system_schema.keyspaces"));
            return rs.Select(r => (string)r["keyspace_name"])
                .Where(n => !system.Contains(n))
                .OrderBy(n => n)
                .ToList();
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            using var cluster = CassandraConnect.Build(target);
            using var session = await cluster.ConnectAsync();
            await session.ExecuteAsync(new SimpleStatement(
                $"CREATE KEYSPACE IF NOT EXISTS \"{name}\" WITH replication = {{'class': 'SimpleStrategy', 'replication_factor': 1}}"));
        }

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (string.Equals(name, target.DefaultDatabase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to drop the instance's default keyspace '{name}'.");
            using var cluster = CassandraConnect.Build(target);
            using var session = await cluster.ConnectAsync();
            await session.ExecuteAsync(new SimpleStatement($"DROP KEYSPACE IF EXISTS \"{name}\""));
        }
    }

    public class CassandraInnerUserService : IInnerUserService
    {
        public virtual string Engine => "cassandra";

        public Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Cassandra user mgmt is not exposed in this version - provisioned instances run with auth disabled.");

        public Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Cassandra user mgmt is not exposed in this version.");

        public Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Cassandra user mgmt is not exposed in this version.");

        public Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Cassandra user mgmt is not exposed in this version.");
    }

    public class CassandraInnerSchemaService : IInnerSchemaService
    {
        public virtual string Engine => "cassandra";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            using var cluster = CassandraConnect.Build(target);
            using var session = await cluster.ConnectAsync();
            var rs = await session.ExecuteAsync(new SimpleStatement(
                "SELECT table_name FROM system_schema.tables WHERE keyspace_name = ?", database));
            return rs.Select(r => new TableSummary((string)r["table_name"], "table")).OrderBy(t => t.Name).ToList();
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            using var cluster = CassandraConnect.Build(target);
            using var session = await cluster.ConnectAsync(database);

            // Cassandra has no OFFSET - emulate by fetching limit+offset and slicing.
            // Reasonable while the UI's offset is small; not a substitute for proper paging.
            var rs = await session.ExecuteAsync(new SimpleStatement($"SELECT * FROM \"{table}\" LIMIT {limit + offset}"));
            var columns = rs.Columns.Select(c => c.Name).ToList();
            var rows = new List<IReadOnlyList<string?>>();
            var i = 0;
            foreach (var r in rs)
            {
                if (i++ < offset) continue;
                var row = new string?[columns.Count];
                for (var c = 0; c < columns.Count; c++)
                {
                    var v = r[c];
                    row[c] = v is null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
                }
                rows.Add(row);
                if (rows.Count >= limit) break;
            }

            // Cassandra COUNT(*) is expensive - skip the totalCount entirely.
            return new TableRows(columns, rows, null);
        }

        public async Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.Values.Count)
                throw new ArgumentException("Columns and values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            using var cluster = CassandraConnect.Build(target);
            using var session = await cluster.ConnectAsync(database);
            var cols = string.Join(", ", request.Columns.Select(c => $"\"{c}\""));
            var placeholders = string.Join(", ", Enumerable.Repeat("?", request.Values.Count));
            var stmt = new SimpleStatement(
                $"INSERT INTO \"{table}\" ({cols}) VALUES ({placeholders})",
                request.Values.Cast<object?>().ToArray());
            await session.ExecuteAsync(stmt);
        }

        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Cassandra row edit not exposed - UPDATE without the full primary key would silently no-op.");

        public Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Cassandra row delete not exposed - DELETE without the full primary key is unsafe.");

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            using var cluster = CassandraConnect.Build(target);
            using var session = await cluster.ConnectAsync(database);
            await session.ExecuteAsync(new SimpleStatement($"DROP TABLE IF EXISTS \"{table}\""));
        }
    }

    // ScyllaDB: same CQL protocol, same driver - just relabel the engine key.
    public sealed class ScyllaDbInnerDatabaseService : CassandraInnerDatabaseService { public override string Engine => "scylladb"; }
    public sealed class ScyllaDbInnerUserService     : CassandraInnerUserService     { public override string Engine => "scylladb"; }
    public sealed class ScyllaDbInnerSchemaService   : CassandraInnerSchemaService   { public override string Engine => "scylladb"; }
}

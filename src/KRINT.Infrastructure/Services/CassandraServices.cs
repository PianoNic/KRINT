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
            var rs = await session.ExecuteAsync(new SimpleStatement( "SELECT keyspace_name FROM system_schema.keyspaces"));
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
            await session.ExecuteAsync(new SimpleStatement( $"CREATE KEYSPACE IF NOT EXISTS \"{name}\" WITH replication = {{'class': 'SimpleStrategy', 'replication_factor': 1}}"));
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
            var rs = await session.ExecuteAsync(new SimpleStatement( "SELECT table_name FROM system_schema.tables WHERE keyspace_name = ?", database));
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
            var (pk, types) = TableSchema(cluster, database, table);

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

            // Surface the primary-key columns so the UI can pin identity and lock them in the editor.
            var columnInfos = columns
                .Select(name => new ColumnInfo(
                    Name: name,
                    Type: types.TryGetValue(name, out var tc) ? tc.ToString().ToLowerInvariant() : "unknown",
                    Nullable: !pk.Contains(name),
                    IsPrimaryKey: pk.Contains(name),
                    IsGenerated: false))
                .ToList();

            // Cassandra COUNT(*) is expensive - skip the totalCount entirely.
            return new TableRows(columns, rows, null, columnInfos);
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
            var (_, types) = TableSchema(cluster, database, table);

            var cols = string.Join(", ", request.Columns.Select(c => $"\"{c}\""));
            var placeholders = string.Join(", ", Enumerable.Repeat("?", request.Values.Count));
            var values = new object?[request.Values.Count];
            for (var i = 0; i < request.Values.Count; i++)
                values[i] = ToCql(request.Values[i], TypeOf(types, request.Columns[i]));

            // INSERT is an upsert in CQL; IF NOT EXISTS makes "create a new row" refuse to clobber an
            // existing one with the same primary key, and lets us report that back to the user.
            var stmt = new SimpleStatement($"INSERT INTO \"{table}\" ({cols}) VALUES ({placeholders}) IF NOT EXISTS", values);
            var rs = await session.ExecuteAsync(stmt);
            if (!Applied(rs))
                throw new InvalidOperationException("A row with this primary key already exists. Edit it instead.");
        }

        public async Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.OriginalValues.Count || request.Columns.Count != request.NewValues.Count)
                throw new ArgumentException("Columns, original and new values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            using var cluster = CassandraConnect.Build(target);
            using var session = await cluster.ConnectAsync(database);
            var (pk, types) = TableSchema(cluster, database, table);
            RequirePrimaryKey(pk, request.Columns);

            var setClauses = new List<string>();
            var args = new List<object?>();
            for (var i = 0; i < request.Columns.Count; i++)
            {
                if (string.Equals(request.OriginalValues[i], request.NewValues[i], StringComparison.Ordinal)) continue;
                if (pk.Contains(request.Columns[i]))
                    throw new InvalidOperationException($"'{request.Columns[i]}' is part of the primary key and can't be edited. Delete the row and insert a new one.");
                setClauses.Add($"\"{request.Columns[i]}\" = ?");
                args.Add(ToCql(request.NewValues[i], TypeOf(types, request.Columns[i])));
            }
            if (setClauses.Count == 0) return;

            var (whereSql, whereArgs) = BuildKeyWhere(pk, request.Columns, request.OriginalValues, types);
            args.AddRange(whereArgs);

            // IF EXISTS turns CQL's blind upsert into a guarded update: a stale edit of a row that's
            // since been deleted reports "not applied" instead of silently re-creating it.
            var stmt = new SimpleStatement($"UPDATE \"{table}\" SET {string.Join(", ", setClauses)} WHERE {whereSql} IF EXISTS", args.ToArray());
            var rs = await session.ExecuteAsync(stmt);
            if (!Applied(rs))
                throw new InvalidOperationException("Row not found - it may have been modified or deleted since you loaded it.");
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.OriginalValues.Count)
                throw new ArgumentException("Columns and values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            using var cluster = CassandraConnect.Build(target);
            using var session = await cluster.ConnectAsync(database);
            var (pk, types) = TableSchema(cluster, database, table);
            RequirePrimaryKey(pk, request.Columns);

            var (whereSql, whereArgs) = BuildKeyWhere(pk, request.Columns, request.OriginalValues, types);
            var stmt = new SimpleStatement($"DELETE FROM \"{table}\" WHERE {whereSql} IF EXISTS", whereArgs.ToArray());
            var rs = await session.ExecuteAsync(stmt);
            if (!Applied(rs))
                throw new InvalidOperationException("Row not found - it may have been modified or deleted since you loaded it.");
        }

        // Pull the table's partition + clustering key names and every column's CQL type from the
        // driver's schema metadata. The full primary key is what makes a CQL UPDATE/DELETE address a
        // single row, and the type codes let us turn the UI's string cells back into bound CQL values.
        private static (HashSet<string> Pk, Dictionary<string, ColumnTypeCode> Types) TableSchema(ICluster cluster, string keyspace, string table)
        {
            var meta = cluster.Metadata.GetTable(keyspace, table)
                ?? throw new ArgumentException($"Table '{table}' not found in keyspace '{keyspace}'.");
            var pk = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in meta.PartitionKeys) pk.Add(c.Name);
            foreach (var c in meta.ClusteringKeys) pk.Add(c.Item1.Name);
            var types = new Dictionary<string, ColumnTypeCode>(StringComparer.Ordinal);
            foreach (var c in meta.TableColumns) types[c.Name] = c.TypeCode;
            return (pk, types);
        }

        private static void RequirePrimaryKey(HashSet<string> pk, IReadOnlyList<string> columns)
        {
            var present = new HashSet<string>(columns, StringComparer.Ordinal);
            var missing = pk.Where(k => !present.Contains(k)).ToList();
            if (missing.Count > 0)
                throw new ArgumentException($"The full primary key is required to address a row; missing: {string.Join(", ", missing)}.");
        }

        private static (string Sql, List<object?> Args) BuildKeyWhere(HashSet<string> pk, IReadOnlyList<string> columns, IReadOnlyList<string?> original, Dictionary<string, ColumnTypeCode> types)
        {
            var clauses = new List<string>();
            var args = new List<object?>();
            for (var i = 0; i < columns.Count; i++)
            {
                if (!pk.Contains(columns[i])) continue;
                clauses.Add($"\"{columns[i]}\" = ?");
                args.Add(ToCql(original[i], TypeOf(types, columns[i])));
            }
            return (string.Join(" AND ", clauses), args);
        }

        private static ColumnTypeCode TypeOf(Dictionary<string, ColumnTypeCode> types, string column)
            => types.TryGetValue(column, out var tc) ? tc : ColumnTypeCode.Text;

        private static bool Applied(RowSet rs)
        {
            // LWT statements (IF EXISTS / IF NOT EXISTS) return one row carrying a "[applied]" boolean;
            // a non-conditional statement returns no rows, which we treat as success.
            var row = rs.FirstOrDefault();
            return row is null || row.GetValue<bool>("[applied]");
        }

        // Convert a grid string cell into the CLR value the driver binds for the column's CQL type.
        private static object? ToCql(string? raw, ColumnTypeCode code)
        {
            if (raw is null) return null;
            var ci = CultureInfo.InvariantCulture;
            return code switch
            {
                ColumnTypeCode.Ascii or ColumnTypeCode.Text or ColumnTypeCode.Varchar => raw,
                ColumnTypeCode.Int => int.Parse(raw, ci),
                ColumnTypeCode.Bigint or ColumnTypeCode.Counter => long.Parse(raw, ci),
                ColumnTypeCode.SmallInt => short.Parse(raw, ci),
                ColumnTypeCode.TinyInt => sbyte.Parse(raw, ci),
                ColumnTypeCode.Boolean => bool.Parse(raw),
                ColumnTypeCode.Float => float.Parse(raw, NumberStyles.Float, ci),
                ColumnTypeCode.Double => double.Parse(raw, NumberStyles.Float, ci),
                ColumnTypeCode.Decimal => decimal.Parse(raw, NumberStyles.Any, ci),
                ColumnTypeCode.Varint => System.Numerics.BigInteger.Parse(raw, ci),
                ColumnTypeCode.Uuid or ColumnTypeCode.Timeuuid => Guid.Parse(raw),
                ColumnTypeCode.Timestamp => DateTimeOffset.Parse(raw, ci, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                ColumnTypeCode.Inet => System.Net.IPAddress.Parse(raw),
                _ => throw new NotSupportedException($"Editing CQL type '{code}' is not supported yet."),
            };
        }

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            using var cluster = CassandraConnect.Build(target);
            using var session = await cluster.ConnectAsync(database);
            await session.ExecuteAsync(new SimpleStatement($"DROP TABLE IF EXISTS \"{table}\""));
        }
    }

}

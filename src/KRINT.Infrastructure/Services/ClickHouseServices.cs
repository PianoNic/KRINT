using System.Globalization;
using ClickHouse.Client.ADO;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // ClickHouse is SQL-shaped but with its own dialect and HTTP-based driver. We talk to it
    // over the HTTP interface (port 8123) via ClickHouse.Client. Row edit/delete use ALTER …
    // UPDATE/DELETE mutations run synchronously (mutations_sync=2) behind a one-row match guard,
    // matching identity on toString(col). Backups (BACKUP TO Disk, clickhouse-backup) are
    // excluded for v1.

    internal static class ClickHouseConnect
    {
        public static ClickHouseConnection Open(InnerDatabaseTarget target, string? overrideDatabase = null)
        {
            // ClickHouse.Client uses the HTTP endpoint. The Docker image binds 8123 (HTTP) and
            // 9000 (native) - our CreateDatabaseCommand publishes 8123.
            var builder = new ClickHouseConnectionStringBuilder
            {
                Host = target.Host,
                Port = (ushort)target.Port,
                Username = target.Username,
                Password = target.Password,
                Database = overrideDatabase ?? target.DefaultDatabase,
                Protocol = "http",
                Timeout = TimeSpan.FromSeconds(5),
            };
            var conn = new ClickHouseConnection(builder.ConnectionString);
            return conn;
        }
    }

    public class ClickHouseInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "clickhouse";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            await using var conn = ClickHouseConnect.Open(target);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM system.databases WHERE name NOT IN ('system','INFORMATION_SCHEMA','information_schema') ORDER BY name";
            var results = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) results.Add(reader.GetString(0));
            return results;
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = ClickHouseConnect.Open(target);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{name}`";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (string.Equals(name, target.DefaultDatabase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to drop the instance's default database '{name}'.");
            await using var conn = ClickHouseConnect.Open(target);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS `{name}`";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public class ClickHouseInnerUserService : IInnerUserService
    {
        public string Engine => "clickhouse";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            await using var conn = ClickHouseConnect.Open(target);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM system.users WHERE storage = 'local_directory' ORDER BY name";
            var results = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) results.Add(reader.GetString(0));
            return results;
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = ClickHouseConnect.Open(target);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE USER `{name}` IDENTIFIED WITH plaintext_password BY '{password.Replace("'", "''")}'";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = ClickHouseConnect.Open(target);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP USER IF EXISTS `{name}`";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = ClickHouseConnect.Open(target);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER USER `{name}` IDENTIFIED WITH plaintext_password BY '{newPassword.Replace("'", "''")}'";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(user);
            InnerDatabaseNameValidator.Require(database);
            await using var conn = ClickHouseConnect.Open(target);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"GRANT ALL ON `{database}`.* TO `{user}`";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public class ClickHouseInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "clickhouse";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            await using var conn = ClickHouseConnect.Open(target, database);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, engine FROM system.tables WHERE database = {database:String} ORDER BY name";
            var p = cmd.CreateParameter(); p.ParameterName = "database"; p.Value = database; cmd.Parameters.Add(p);
            var results = new List<TableSummary>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                var engine = reader.GetString(1);
                var kind = engine.Contains("View", StringComparison.OrdinalIgnoreCase) ? "view" : "table";
                results.Add(new TableSummary(name, kind));
            }
            return results;
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            await using var conn = ClickHouseConnect.Open(target, database);
            await conn.OpenAsync(cancellationToken);

            long? total = null;
            await using (var countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = $"SELECT count() FROM `{table}`";
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                if (raw is not null && raw is not DBNull) total = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            }

            // Render every column to text server-side with toString() so the strings the grid loads
            // are exactly what the row-identity WHERE compares against (toString(col) = @original).
            // This is the same reason the SQL engines cast to text - .NET formatting of typed values
            // (bool -> "True", DateTime -> .NET format) doesn't match ClickHouse's own rendering.
            var colMeta = await LoadColumnsAsync(conn, database, table, cancellationToken);
            var selectList = colMeta.Count == 0
                ? "*"
                : string.Join(", ", colMeta.Select(c => $"toString(`{c.Name}`) AS `{c.Name}`"));

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {selectList} FROM `{table}` LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var columns = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));

            var rows = new List<IReadOnlyList<string?>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new string?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var v = reader.GetValue(i);
                    row[i] = v is DBNull ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
                }
                rows.Add(row);
            }

            // Surface sorting-key columns so the UI can pin identity and lock them in the editor
            // (ALTER UPDATE can't modify key columns).
            var columnInfos = colMeta.Count == 0
                ? null
                : colMeta.Select(c => new ColumnInfo(c.Name, c.Type, !c.IsKey, c.IsKey, false)).ToList();
            return new TableRows(columns, rows, total, columnInfos);
        }

        // Ordered columns with their CQL-ish type string and whether they're part of the primary
        // (sorting) key. Drives both the text-cast SELECT and the row-identity WHERE.
        private static async Task<IReadOnlyList<(string Name, string Type, bool IsKey)>> LoadColumnsAsync(
            ClickHouseConnection conn, string database, string table, CancellationToken cancellationToken)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, type, is_in_primary_key FROM system.columns WHERE database = {d:String} AND table = {t:String} ORDER BY position";
            var pd = cmd.CreateParameter(); pd.ParameterName = "d"; pd.Value = database; cmd.Parameters.Add(pd);
            var pt = cmd.CreateParameter(); pt.ParameterName = "t"; pt.Value = table; cmd.Parameters.Add(pt);
            var cols = new List<(string, string, bool)>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                cols.Add((reader.GetString(0), reader.GetString(1), Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) != 0));
            return cols;
        }

        // Identity WHERE on the loaded values, matching the toString() rendering used by the read.
        private static string BuildIdentityWhere(System.Data.Common.DbCommand cmd, IReadOnlyList<string> columns, IReadOnlyList<string?> original)
        {
            var clauses = new List<string>();
            for (var i = 0; i < columns.Count; i++)
            {
                InnerDatabaseNameValidator.Require(columns[i]);
                if (original[i] is null)
                {
                    clauses.Add($"`{columns[i]}` IS NULL");
                }
                else
                {
                    var p = cmd.CreateParameter(); p.ParameterName = $"w{i}"; p.Value = original[i]; cmd.Parameters.Add(p);
                    clauses.Add($"toString(`{columns[i]}`) = {{w{i}:String}}");
                }
            }
            return string.Join(" AND ", clauses);
        }

        private static async Task<long> CountMatchesAsync(ClickHouseConnection conn, string table, IReadOnlyList<string> columns, IReadOnlyList<string?> original, CancellationToken cancellationToken)
        {
            await using var cmd = conn.CreateCommand();
            var where = BuildIdentityWhere(cmd, columns, original);
            cmd.CommandText = $"SELECT count() FROM `{table}` WHERE {where}";
            var raw = await cmd.ExecuteScalarAsync(cancellationToken);
            return raw is null or DBNull ? 0 : Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }

        public async Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.Values.Count)
                throw new ArgumentException("Columns and values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            await using var conn = ClickHouseConnect.Open(target, database);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            var cols = string.Join(", ", request.Columns.Select(c => $"`{c}`"));
            var placeholders = new List<string>();
            for (var i = 0; i < request.Values.Count; i++)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = $"v{i}";
                p.Value = (object?)request.Values[i] ?? DBNull.Value;
                cmd.Parameters.Add(p);
                placeholders.Add($"{{v{i}:String}}");
            }
            cmd.CommandText = $"INSERT INTO `{table}` ({cols}) VALUES ({string.Join(", ", placeholders)})";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // ClickHouse edit/delete go through ALTER ... UPDATE/DELETE mutations. They lack a built-in
        // single-row guarantee, so we enforce one ourselves: a match-count guard refuses anything but
        // exactly one matching row, then the mutation runs with mutations_sync=2 (block until applied).
        public async Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.OriginalValues.Count || request.Columns.Count != request.NewValues.Count)
                throw new ArgumentException("Columns, original and new values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            await using var conn = ClickHouseConnect.Open(target, database);
            await conn.OpenAsync(cancellationToken);
            var typeByName = (await LoadColumnsAsync(conn, database, table, cancellationToken))
                .ToDictionary(c => c.Name, c => (c.Type, c.IsKey), StringComparer.Ordinal);

            await using var cmd = conn.CreateCommand();
            var setClauses = new List<string>();
            for (var i = 0; i < request.Columns.Count; i++)
            {
                if (string.Equals(request.OriginalValues[i], request.NewValues[i], StringComparison.Ordinal)) continue;
                if (typeByName.TryGetValue(request.Columns[i], out var meta) && meta.IsKey)
                    throw new InvalidOperationException($"'{request.Columns[i]}' is part of the sorting key and can't be edited. Delete the row and insert a new one.");
                var type = typeByName.TryGetValue(request.Columns[i], out var m) ? m.Type : "String";
                var p = cmd.CreateParameter(); p.ParameterName = $"s{i}"; p.Value = (object?)request.NewValues[i] ?? DBNull.Value; cmd.Parameters.Add(p);
                // CAST the bound string back to the column's declared type so typed columns update cleanly.
                setClauses.Add($"`{request.Columns[i]}` = CAST({{s{i}:String}}, '{type}')");
            }
            if (setClauses.Count == 0) return;

            var matches = await CountMatchesAsync(conn, table, request.Columns, request.OriginalValues, cancellationToken);
            if (matches == 0) throw new InvalidOperationException("Row not found - it may have been modified or deleted since you loaded it.");
            if (matches > 1) throw new InvalidOperationException("Ambiguous: more than one row matches the original values. Refusing to update.");

            var where = BuildIdentityWhere(cmd, request.Columns, request.OriginalValues);
            cmd.CommandText = $"ALTER TABLE `{table}` UPDATE {string.Join(", ", setClauses)} WHERE {where} SETTINGS mutations_sync = 2";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.OriginalValues.Count)
                throw new ArgumentException("Columns and values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            await using var conn = ClickHouseConnect.Open(target, database);
            await conn.OpenAsync(cancellationToken);

            var matches = await CountMatchesAsync(conn, table, request.Columns, request.OriginalValues, cancellationToken);
            if (matches == 0) throw new InvalidOperationException("Row not found - it may have been modified or deleted since you loaded it.");
            if (matches > 1) throw new InvalidOperationException("Ambiguous: more than one row matches. Refusing to delete.");

            await using var cmd = conn.CreateCommand();
            var where = BuildIdentityWhere(cmd, request.Columns, request.OriginalValues);
            cmd.CommandText = $"ALTER TABLE `{table}` DELETE WHERE {where} SETTINGS mutations_sync = 2";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            await using var conn = ClickHouseConnect.Open(target, database);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP TABLE IF EXISTS `{table}`";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

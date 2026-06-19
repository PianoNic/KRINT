using System.Globalization;
using KRINT.Infrastructure.Interfaces;
using MySqlConnector;

namespace KRINT.Infrastructure.Services
{
    public class MySqlInnerSchemaService : IInnerSchemaService
    {
        public virtual string Engine => "mysql";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var cmd = new MySqlCommand("SELECT table_name, table_type FROM information_schema.tables WHERE table_schema = @schema ORDER BY table_name", conn);
            cmd.Parameters.AddWithValue("@schema", database);

            var results = new List<TableSummary>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                var rawKind = reader.GetString(1);
                var kind = rawKind.Contains("VIEW", StringComparison.OrdinalIgnoreCase) ? "view" : "table";
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

            await using var conn = await OpenAsync(target, database, cancellationToken);

            long? total = null;
            await using (var countCmd = new MySqlCommand($"SELECT COUNT(*) FROM `{table}`", conn))
            {
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                if (raw is not null && raw is not DBNull) total = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            }

            var columnInfos = await FetchColumnInfosAsync(conn, database, table, cancellationToken);

            // Render every column to text server-side with the same CAST(... AS CHAR) the row-identity
            // WHERE clause uses, so the strings the grid loads round-trip exactly. Reading typed values
            // and formatting them in .NET (tinyint(1) -> "True", DATETIME -> .NET format) disagreed with
            // MySQL's CAST ("1", "2026-06-19 12:34:56"), so edits/deletes matched zero rows.
            var selectList = columnInfos.Count == 0
                ? "*"
                : string.Join(", ", columnInfos.Select(c => $"CAST(`{c.Name}` AS CHAR) AS `{c.Name}`"));

            await using var cmd = new MySqlCommand($"SELECT {selectList} FROM `{table}` LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}", conn);
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
            return new TableRows(columns, rows, total, columnInfos);
        }

        private static async Task<IReadOnlyList<ColumnInfo>> FetchColumnInfosAsync(MySqlConnection conn, string database, string table, CancellationToken cancellationToken)
        {
            await using var cmd = new MySqlCommand("""
                SELECT  column_name,
                        column_type,
                        is_nullable = 'YES'                                  AS nullable,
                        column_key = 'PRI'                                   AS is_pk,
                        extra LIKE '%auto_increment%' OR extra LIKE '%GENERATED%' AS is_generated
                FROM    information_schema.columns
                WHERE   table_schema = @s AND table_name = @t
                ORDER BY ordinal_position;
            """, conn);
            cmd.Parameters.AddWithValue("@s", database);
            cmd.Parameters.AddWithValue("@t", table);
            var results = new List<ColumnInfo>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new ColumnInfo(
                    Name: reader.GetString(0),
                    Type: reader.GetString(1),
                    Nullable: reader.GetBoolean(2),
                    IsPrimaryKey: reader.GetBoolean(3),
                    IsGenerated: reader.GetBoolean(4)));
            }
            return results;
        }

        public async Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            UpdateRowRequestValidator.Require(request);

            var changedIndexes = new List<int>();
            for (var i = 0; i < request.Columns.Count; i++)
            {
                if (!string.Equals(request.OriginalValues[i], request.NewValues[i], StringComparison.Ordinal))
                    changedIndexes.Add(i);
            }
            if (changedIndexes.Count == 0) return;

            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            var setClauses = new List<string>();
            var whereClauses = new List<string>();
            await using var cmd = new MySqlCommand { Connection = conn, Transaction = tx };

            for (var k = 0; k < changedIndexes.Count; k++)
            {
                var i = changedIndexes[k];
                var p = $"@n{k}";
                setClauses.Add($"`{request.Columns[i]}` = {p}");
                cmd.Parameters.AddWithValue(p, (object?)request.NewValues[i] ?? DBNull.Value);
            }
            for (var i = 0; i < request.Columns.Count; i++)
            {
                var col = $"`{request.Columns[i]}`";
                if (request.OriginalValues[i] is null)
                {
                    whereClauses.Add($"{col} IS NULL");
                }
                else
                {
                    var p = $"@o{i}";
                    whereClauses.Add($"CAST({col} AS CHAR) = {p}");
                    cmd.Parameters.AddWithValue(p, request.OriginalValues[i]!);
                }
            }

            // MySQL supports UPDATE ... LIMIT - combine with a match-count check to enforce exactly one.
            await using (var countCmd = new MySqlCommand( $"SELECT COUNT(*) FROM (SELECT 1 FROM `{table}` WHERE {string.Join(" AND ", whereClauses)} LIMIT 2) s", conn, tx))
            {
                foreach (MySqlParameter param in cmd.Parameters)
                {
                    if (param.ParameterName.StartsWith("@o", StringComparison.Ordinal))
                        countCmd.Parameters.AddWithValue(param.ParameterName, param.Value!);
                }
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                var matches = raw is null || raw is DBNull ? 0 : Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                if (matches == 0) throw new InvalidOperationException("Row not found - it may have been modified or deleted since you loaded it.");
                if (matches > 1) throw new InvalidOperationException("Ambiguous: more than one row matches the original values. Refusing to update.");
            }

            cmd.CommandText = $"UPDATE `{table}` SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)} LIMIT 1";
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1) throw new InvalidOperationException($"Expected to update 1 row, got {affected}.");
            await tx.CommitAsync(cancellationToken);
        }

        public async Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.Values.Count)
                throw new ArgumentException("Columns and values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            await using var conn = await OpenAsync(target, database, cancellationToken);
            var cols = string.Join(", ", request.Columns.Select(c => $"`{c}`"));
            var placeholders = string.Join(", ", request.Values.Select((_, i) => $"@v{i}"));
            await using var cmd = new MySqlCommand($"INSERT INTO `{table}` ({cols}) VALUES ({placeholders})", conn);
            for (var i = 0; i < request.Values.Count; i++)
                cmd.Parameters.AddWithValue($"@v{i}", (object?)request.Values[i] ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.OriginalValues.Count)
                throw new ArgumentException("Columns and values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            var whereClauses = new List<string>();
            await using var cmd = new MySqlCommand { Connection = conn, Transaction = tx };
            for (var i = 0; i < request.Columns.Count; i++)
            {
                var col = $"`{request.Columns[i]}`";
                if (request.OriginalValues[i] is null)
                {
                    whereClauses.Add($"{col} IS NULL");
                }
                else
                {
                    var p = $"@o{i}";
                    whereClauses.Add($"CAST({col} AS CHAR) = {p}");
                    cmd.Parameters.AddWithValue(p, request.OriginalValues[i]!);
                }
            }

            await using (var countCmd = new MySqlCommand( $"SELECT COUNT(*) FROM (SELECT 1 FROM `{table}` WHERE {string.Join(" AND ", whereClauses)} LIMIT 2) s", conn, tx))
            {
                foreach (MySqlParameter param in cmd.Parameters)
                    if (param.ParameterName.StartsWith("@o", StringComparison.Ordinal))
                        countCmd.Parameters.AddWithValue(param.ParameterName, param.Value!);
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                var matches = raw is null || raw is DBNull ? 0 : Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                if (matches == 0) throw new InvalidOperationException("Row not found - it may have been modified or deleted since you loaded it.");
                if (matches > 1) throw new InvalidOperationException("Ambiguous: more than one row matches. Refusing to delete.");
            }

            cmd.CommandText = $"DELETE FROM `{table}` WHERE {string.Join(" AND ", whereClauses)} LIMIT 1";
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1) throw new InvalidOperationException($"Expected to delete 1 row, got {affected}.");
            await tx.CommitAsync(cancellationToken);
        }

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var cmd = new MySqlCommand($"DROP TABLE IF EXISTS `{table}`", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<MySqlConnection> OpenAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken)
        {
            var csb = new MySqlConnectionStringBuilder
            {
                Server = target.Host,
                Port = (uint)target.Port,
                UserID = target.Username,
                Password = target.Password,
                Database = database,
                ConnectionTimeout = 5,
            };
            var conn = new MySqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}

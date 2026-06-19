using System.Globalization;
using KRINT.Infrastructure.Interfaces;
using Npgsql;

namespace KRINT.Infrastructure.Services
{
    public class PostgresInnerSchemaService : IInnerSchemaService
    {
        public virtual string Engine => "postgres";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT table_schema, table_name, table_type FROM information_schema.tables " + "WHERE table_schema NOT IN ('pg_catalog','information_schema') " + "ORDER BY table_schema, table_name", conn);
            var results = new List<TableSummary>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // Tables outside "public" are exposed as "schema.name" so row operations can
                // address them; the bare name only resolves in public via search_path.
                var schema = reader.GetString(0);
                var name = schema == "public" ? reader.GetString(1) : $"{schema}.{reader.GetString(1)}";
                var kind = reader.GetString(2) == "VIEW" ? "view" : "table";
                results.Add(new TableSummary(name, kind));
            }
            return results;
        }

        public async Task<TableRows> FetchRowsAsync(InnerDatabaseTarget target, string database, string table, int limit, int offset, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var qualified = QualifyTable(table);
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(0, offset);

            await using var conn = await OpenAsync(target, database, cancellationToken);

            long? total = null;
            await using (var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {qualified}", conn))
            {
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                if (raw is long l) total = l;
            }

            var columnInfos = await FetchColumnInfosAsync(conn, table, cancellationToken);

            // Render every column to text server-side so the strings the grid loads are exactly what
            // the row-identity WHERE clause compares against ("col"::text = @original). Reading typed
            // values and formatting them in .NET (bool -> "True", timestamps -> .NET format, arrays ->
            // "System.String[]") disagreed with Postgres's own ::text rendering ("true", ISO dates),
            // so every later UPDATE/DELETE matched zero rows and failed with "Row not found".
            var selectList = columnInfos.Count == 0
                ? "*"
                : string.Join(", ", columnInfos.Select(c => $"\"{c.Name}\"::text AS \"{c.Name}\""));

            await using var cmd = new NpgsqlCommand($"SELECT {selectList} FROM {qualified} LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}", conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            return await ReadRowsAsync(reader, total, columnInfos, cancellationToken);
        }

        private static async Task<IReadOnlyList<ColumnInfo>> FetchColumnInfosAsync(NpgsqlConnection conn, string table, CancellationToken cancellationToken)
        {
            var (schema, name) = SplitTable(table);
            // Joining information_schema.columns with pg_constraint (via pg_attribute) so we can
            // surface IsPrimaryKey + IsGenerated to the UI without per-column round-trips.
            await using var cmd = new NpgsqlCommand("""
                SELECT  c.column_name,
                        c.udt_name,
                        c.is_nullable = 'YES'                    AS nullable,
                        COALESCE(pk.is_pk, FALSE)                AS is_pk,
                        c.is_identity = 'YES' OR c.is_generated <> 'NEVER'
                            OR c.column_default LIKE 'nextval(%'        AS is_generated
                FROM    information_schema.columns c
                LEFT JOIN (
                    SELECT a.attname AS column_name, TRUE AS is_pk
                    FROM   pg_index i
                    JOIN   pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
                    WHERE  i.indrelid = (quote_ident(@s) || '.' || quote_ident(@t))::regclass AND i.indisprimary
                ) pk ON pk.column_name = c.column_name
                WHERE  c.table_schema = @s
                AND    c.table_name = @t
                ORDER BY c.ordinal_position;
            """, conn);
            cmd.Parameters.AddWithValue("@s", schema);
            cmd.Parameters.AddWithValue("@t", name);
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
            var qualified = QualifyTable(table);
            UpdateRowRequestValidator.Require(request);

            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
            await ApplyUpdateAsync(conn, tx, qualified, request, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        /// <summary>Wraps every row update in a single transaction so the user's Save is
        /// all-or-nothing: any per-row failure rolls back the whole batch.</summary>
        public async Task BulkUpdateRowsAsync(InnerDatabaseTarget target, string database, string table, BulkUpdateRowsRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var qualified = QualifyTable(table);
            if (request.Updates.Count == 0) return;
            foreach (var u in request.Updates) UpdateRowRequestValidator.Require(u);

            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
            foreach (var update in request.Updates)
                await ApplyUpdateAsync(conn, tx, qualified, update, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        // The actual UPDATE/DELETE statements that pin the single matched row. Postgres uses ctid
        // (its physical row pointer); engines without ctid (CockroachDB) override these. Safe because
        // the caller's match-count guard has already proven exactly one row matches the WHERE.
        protected virtual string BuildUpdateCommandText(string qualifiedTable, string setClause, string whereClause)
            => $"UPDATE {qualifiedTable} SET {setClause} WHERE ctid = (SELECT ctid FROM {qualifiedTable} WHERE {whereClause} LIMIT 2)";

        protected virtual string BuildDeleteCommandText(string qualifiedTable, string whereClause)
            => $"DELETE FROM {qualifiedTable} WHERE ctid = (SELECT ctid FROM {qualifiedTable} WHERE {whereClause} LIMIT 1)";

        private async Task ApplyUpdateAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string qualifiedTable, UpdateRowRequest request, CancellationToken cancellationToken)
        {
            var changedIndexes = new List<int>();
            for (var i = 0; i < request.Columns.Count; i++)
            {
                if (!string.Equals(request.OriginalValues[i], request.NewValues[i], StringComparison.Ordinal))
                    changedIndexes.Add(i);
            }
            if (changedIndexes.Count == 0) return;

            var setClauses = new List<string>();
            var whereClauses = new List<string>();
            await using var cmd = new NpgsqlCommand { Connection = conn, Transaction = tx };

            for (var k = 0; k < changedIndexes.Count; k++)
            {
                var i = changedIndexes[k];
                InnerDatabaseNameValidator.Require(request.Columns[i]);
                var p = $"@n{k}";
                setClauses.Add($"\"{request.Columns[i]}\" = {p}");
                cmd.Parameters.AddWithValue(p, (object?)request.NewValues[i] ?? DBNull.Value);
            }
            for (var i = 0; i < request.Columns.Count; i++)
            {
                InnerDatabaseNameValidator.Require(request.Columns[i]);
                var col = $"\"{request.Columns[i]}\"";
                if (request.OriginalValues[i] is null)
                {
                    whereClauses.Add($"{col} IS NULL");
                }
                else
                {
                    var p = $"@o{i}";
                    whereClauses.Add($"{col}::text = {p}");
                    cmd.Parameters.AddWithValue(p, request.OriginalValues[i]!);
                }
            }

            cmd.CommandText = BuildUpdateCommandText(qualifiedTable, string.Join(", ", setClauses), string.Join(" AND ", whereClauses));

            // Two-step strategy: peek matches first to enforce "exactly one row".
            await using (var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM (SELECT 1 FROM {qualifiedTable} WHERE {string.Join(" AND ", whereClauses)} LIMIT 2) s", conn, tx))
            {
                foreach (NpgsqlParameter param in cmd.Parameters)
                {
                    if (param.ParameterName.StartsWith("@o", StringComparison.Ordinal))
                        countCmd.Parameters.AddWithValue(param.ParameterName, param.Value!);
                }
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                var matches = raw is long l ? l : 0;
                if (matches == 0) throw new InvalidOperationException("Row not found - it may have been modified or deleted since you loaded it.");
                if (matches > 1) throw new InvalidOperationException("Ambiguous: more than one row matches the original values. Refusing to update.");
            }

            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1) throw new InvalidOperationException($"Expected to update 1 row, got {affected}.");
        }

        public async Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var qualified = QualifyTable(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.Values.Count)
                throw new ArgumentException("Columns and values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            await using var conn = await OpenAsync(target, database, cancellationToken);
            var cols = string.Join(", ", request.Columns.Select(c => $"\"{c}\""));
            var placeholders = string.Join(", ", request.Values.Select((_, i) => $"@v{i}"));
            await using var cmd = new NpgsqlCommand($"INSERT INTO {qualified} ({cols}) VALUES ({placeholders})", conn);
            for (var i = 0; i < request.Values.Count; i++)
                cmd.Parameters.AddWithValue($"@v{i}", (object?)request.Values[i] ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var qualified = QualifyTable(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.OriginalValues.Count)
                throw new ArgumentException("Columns and values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            var whereClauses = new List<string>();
            await using var cmd = new NpgsqlCommand { Connection = conn, Transaction = tx };
            for (var i = 0; i < request.Columns.Count; i++)
            {
                var col = $"\"{request.Columns[i]}\"";
                if (request.OriginalValues[i] is null)
                {
                    whereClauses.Add($"{col} IS NULL");
                }
                else
                {
                    var p = $"@o{i}";
                    whereClauses.Add($"{col}::text = {p}");
                    cmd.Parameters.AddWithValue(p, request.OriginalValues[i]!);
                }
            }

            // Match-count guard - refuse to delete if zero or >1 rows match the row the UI sent.
            await using (var countCmd = new NpgsqlCommand( $"SELECT COUNT(*) FROM (SELECT 1 FROM {qualified} WHERE {string.Join(" AND ", whereClauses)} LIMIT 2) s", conn, tx))
            {
                foreach (NpgsqlParameter param in cmd.Parameters)
                    if (param.ParameterName.StartsWith("@o", StringComparison.Ordinal))
                        countCmd.Parameters.AddWithValue(param.ParameterName, param.Value!);
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                var matches = raw is long l ? l : 0;
                if (matches == 0) throw new InvalidOperationException("Row not found - it may have been modified or deleted since you loaded it.");
                if (matches > 1) throw new InvalidOperationException("Ambiguous: more than one row matches. Refusing to delete.");
            }

            cmd.CommandText = BuildDeleteCommandText(qualified, string.Join(" AND ", whereClauses));
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1) throw new InvalidOperationException($"Expected to delete 1 row, got {affected}.");
            await tx.CommitAsync(cancellationToken);
        }

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var qualified = QualifyTable(table);
            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var cmd = new NpgsqlCommand($"DROP TABLE IF EXISTS {qualified}", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Browse exposes non-public tables as "schema.name" (see ListTablesAsync); a bare name
        // means public. Both parts go through the identifier validator before interpolation.
        public static (string Schema, string Name) SplitTable(string table)
        {
            var i = table.IndexOf('.');
            var (schema, name) = i < 0 ? ("public", table) : (table[..i], table[(i + 1)..]);
            InnerDatabaseNameValidator.Require(schema);
            InnerDatabaseNameValidator.Require(name);
            return (schema, name);
        }

        public static string QualifyTable(string table)
        {
            var (schema, name) = SplitTable(table);
            return $"\"{schema}\".\"{name}\"";
        }

        // Every column comes back as text (cast in the SELECT), so the reader always yields strings.
        private static async Task<TableRows> ReadRowsAsync(NpgsqlDataReader reader, long? total, IReadOnlyList<ColumnInfo>? columnInfos, CancellationToken cancellationToken)
        {
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

        private static async Task<NpgsqlConnection> OpenAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken)
        {
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = target.Host,
                Port = target.Port,
                Username = target.Username,
                Password = target.Password,
                Database = database,
                Timeout = 5,
                Pooling = false,
            };
            var conn = new NpgsqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}

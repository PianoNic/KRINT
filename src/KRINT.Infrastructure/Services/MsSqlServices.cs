using System.Globalization;
using KRINT.Infrastructure.Interfaces;
using Microsoft.Data.SqlClient;

namespace KRINT.Infrastructure.Services
{
    // Microsoft SQL Server. Uses Microsoft.Data.SqlClient (TDS). Engine concepts map cleanly:
    // databases, logins/users, schemas/tables, rows. Backup deferred (would use BACKUP DATABASE
    // TO DISK and read the bak file out of the container).

    internal static class MsSqlConnect
    {
        public static async Task<SqlConnection> OpenAsync(InnerDatabaseTarget target, string? overrideDatabase, CancellationToken cancellationToken)
        {
            var csb = new SqlConnectionStringBuilder
            {
                DataSource = $"{target.Host},{target.Port}",
                UserID = target.Username,
                Password = target.Password,
                InitialCatalog = overrideDatabase ?? target.DefaultDatabase,
                TrustServerCertificate = true,
                ConnectTimeout = 5,
                Pooling = false,
            };
            var conn = new SqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }

    public class MsSqlInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "mssql";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            await using var conn = await MsSqlConnect.OpenAsync(target, "master", cancellationToken);
            await using var cmd = new SqlCommand(
                "SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name",
                conn);
            var results = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) results.Add(reader.GetString(0));
            return results;
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = await MsSqlConnect.OpenAsync(target, "master", cancellationToken);
            await using var cmd = new SqlCommand($"CREATE DATABASE [{name}]", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (string.Equals(name, target.DefaultDatabase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to drop the instance's default database '{name}'.");
            await using var conn = await MsSqlConnect.OpenAsync(target, "master", cancellationToken);
            // Kick connections first, then drop. Otherwise active sessions hold the lock.
            await using var cmd = new SqlCommand(
                $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]",
                conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public class MsSqlInnerUserService : IInnerUserService
    {
        public string Engine => "mssql";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            await using var conn = await MsSqlConnect.OpenAsync(target, "master", cancellationToken);
            await using var cmd = new SqlCommand(
                "SELECT name FROM sys.sql_logins WHERE is_disabled = 0 AND name NOT LIKE '##%' AND name <> 'sa' ORDER BY name",
                conn);
            var results = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) results.Add(reader.GetString(0));
            return results;
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = await MsSqlConnect.OpenAsync(target, "master", cancellationToken);
            // CREATE LOGIN - cannot parameterise password literal in TDS; escape single quotes.
            var safePw = password.Replace("'", "''");
            await using var cmd = new SqlCommand($"CREATE LOGIN [{name}] WITH PASSWORD = '{safePw}'", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = await MsSqlConnect.OpenAsync(target, "master", cancellationToken);
            await using var cmd = new SqlCommand($"DROP LOGIN [{name}]", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = await MsSqlConnect.OpenAsync(target, "master", cancellationToken);
            var safePw = newPassword.Replace("'", "''");
            await using var cmd = new SqlCommand($"ALTER LOGIN [{name}] WITH PASSWORD = '{safePw}'", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(user);
            InnerDatabaseNameValidator.Require(database);
            await using var conn = await MsSqlConnect.OpenAsync(target, database, cancellationToken);
            // Add the login as a user in this DB, then grant db_owner. Idempotent-ish via IF NOT EXISTS.
            var sql = $@"
                IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '{user}')
                    CREATE USER [{user}] FOR LOGIN [{user}];
                ALTER ROLE db_owner ADD MEMBER [{user}];";
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public class MsSqlInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "mssql";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            await using var conn = await MsSqlConnect.OpenAsync(target, database, cancellationToken);
            await using var cmd = new SqlCommand(
                "SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' ORDER BY TABLE_NAME",
                conn);
            var results = new List<TableSummary>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                var kind = reader.GetString(1).Equals("VIEW", StringComparison.OrdinalIgnoreCase) ? "view" : "table";
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

            await using var conn = await MsSqlConnect.OpenAsync(target, database, cancellationToken);

            long? total = null;
            await using (var countCmd = new SqlCommand($"SELECT COUNT(*) FROM [{table}]", conn))
            {
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                if (raw is int i) total = i;
                else if (raw is long l) total = l;
            }

            // OFFSET/FETCH requires an ORDER BY; sort by the first column to get a stable page.
            await using var cmd = new SqlCommand(
                $"SELECT * FROM [{table}] ORDER BY (SELECT NULL) OFFSET {offset.ToString(CultureInfo.InvariantCulture)} ROWS FETCH NEXT {limit.ToString(CultureInfo.InvariantCulture)} ROWS ONLY",
                conn);
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
            return new TableRows(columns, rows, total);
        }

        public async Task InsertRowAsync(InnerDatabaseTarget target, string database, string table, InsertRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0 || request.Columns.Count != request.Values.Count)
                throw new ArgumentException("Columns and values must have the same non-zero length.", nameof(request));
            foreach (var c in request.Columns) InnerDatabaseNameValidator.Require(c);

            await using var conn = await MsSqlConnect.OpenAsync(target, database, cancellationToken);
            var cols = string.Join(", ", request.Columns.Select(c => $"[{c}]"));
            var placeholders = string.Join(", ", request.Values.Select((_, i) => $"@v{i}"));
            await using var cmd = new SqlCommand($"INSERT INTO [{table}] ({cols}) VALUES ({placeholders})", conn);
            for (var i = 0; i < request.Values.Count; i++)
                cmd.Parameters.AddWithValue($"@v{i}", (object?)request.Values[i] ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            if (request.Columns.Count == 0) throw new ArgumentException("Columns required.", nameof(request));

            var changedIndexes = new List<int>();
            for (var i = 0; i < request.Columns.Count; i++)
                if (!string.Equals(request.OriginalValues[i], request.NewValues[i], StringComparison.Ordinal))
                    changedIndexes.Add(i);
            if (changedIndexes.Count == 0) return;

            await using var conn = await MsSqlConnect.OpenAsync(target, database, cancellationToken);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);

            var setClauses = new List<string>();
            var whereClauses = new List<string>();
            await using var cmd = new SqlCommand { Connection = conn, Transaction = tx };

            for (var k = 0; k < changedIndexes.Count; k++)
            {
                var i = changedIndexes[k];
                InnerDatabaseNameValidator.Require(request.Columns[i]);
                setClauses.Add($"[{request.Columns[i]}] = @n{k}");
                cmd.Parameters.AddWithValue($"@n{k}", (object?)request.NewValues[i] ?? DBNull.Value);
            }
            for (var i = 0; i < request.Columns.Count; i++)
            {
                InnerDatabaseNameValidator.Require(request.Columns[i]);
                var col = $"[{request.Columns[i]}]";
                if (request.OriginalValues[i] is null) whereClauses.Add($"{col} IS NULL");
                else
                {
                    whereClauses.Add($"CAST({col} AS NVARCHAR(MAX)) = @o{i}");
                    cmd.Parameters.AddWithValue($"@o{i}", request.OriginalValues[i]!);
                }
            }

            // Match guard.
            await using (var countCmd = new SqlCommand(
                $"SELECT COUNT(*) FROM (SELECT TOP 2 1 c FROM [{table}] WHERE {string.Join(" AND ", whereClauses)}) s",
                conn, tx))
            {
                foreach (SqlParameter p in cmd.Parameters)
                    if (p.ParameterName.StartsWith("@o", StringComparison.Ordinal))
                        countCmd.Parameters.AddWithValue(p.ParameterName, p.Value!);
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                var matches = raw is int i ? i : 0;
                if (matches == 0) throw new InvalidOperationException("Row not found - it may have been modified or deleted since you loaded it.");
                if (matches > 1) throw new InvalidOperationException("Ambiguous: more than one row matches. Refusing to update.");
            }

            // TOP (1) caps the update to a single row to match the guard.
            cmd.CommandText = $"UPDATE TOP (1) [{table}] SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1) throw new InvalidOperationException($"Expected to update 1 row, got {affected}.");
            await tx.CommitAsync(cancellationToken);
        }

        public async Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);

            await using var conn = await MsSqlConnect.OpenAsync(target, database, cancellationToken);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken);

            var whereClauses = new List<string>();
            await using var cmd = new SqlCommand { Connection = conn, Transaction = tx };
            for (var i = 0; i < request.Columns.Count; i++)
            {
                InnerDatabaseNameValidator.Require(request.Columns[i]);
                var col = $"[{request.Columns[i]}]";
                if (request.OriginalValues[i] is null) whereClauses.Add($"{col} IS NULL");
                else
                {
                    whereClauses.Add($"CAST({col} AS NVARCHAR(MAX)) = @o{i}");
                    cmd.Parameters.AddWithValue($"@o{i}", request.OriginalValues[i]!);
                }
            }

            await using (var countCmd = new SqlCommand(
                $"SELECT COUNT(*) FROM (SELECT TOP 2 1 c FROM [{table}] WHERE {string.Join(" AND ", whereClauses)}) s",
                conn, tx))
            {
                foreach (SqlParameter p in cmd.Parameters) countCmd.Parameters.AddWithValue(p.ParameterName, p.Value!);
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                var matches = raw is int i ? i : 0;
                if (matches == 0) throw new InvalidOperationException("Row not found.");
                if (matches > 1) throw new InvalidOperationException("Ambiguous: more than one row matches. Refusing to delete.");
            }

            cmd.CommandText = $"DELETE TOP (1) FROM [{table}] WHERE {string.Join(" AND ", whereClauses)}";
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1) throw new InvalidOperationException($"Expected to delete 1 row, got {affected}.");
            await tx.CommitAsync(cancellationToken);
        }

        public async Task DropTableAsync(InnerDatabaseTarget target, string database, string table, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            InnerDatabaseNameValidator.Require(table);
            await using var conn = await MsSqlConnect.OpenAsync(target, database, cancellationToken);
            await using var cmd = new SqlCommand($"DROP TABLE IF EXISTS [{table}]", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

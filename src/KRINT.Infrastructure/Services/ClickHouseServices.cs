using System.Globalization;
using ClickHouse.Client.ADO;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // ClickHouse is SQL-shaped but with its own dialect and HTTP-based driver. We talk to it
    // over the HTTP interface (port 8123) via ClickHouse.Client. Row edit/delete are excluded
    // from caps because ALTER … UPDATE/DELETE are async mutations with no "exactly-one-row"
    // guarantee without primary-key knowledge. Backups (BACKUP TO Disk, clickhouse-backup)
    // are also excluded for v1.

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

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM `{table}` LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}";
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

        // SupportsRowEdit / SupportsRowDelete / SupportsBackup are off in the capabilities map,
        // so the UI never calls these - they throw to make any accidental call obvious.
        public Task UpdateRowAsync(InnerDatabaseTarget target, string database, string table, UpdateRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Row edit is not exposed for ClickHouse - ALTER UPDATE is asynchronous and lacks a single-row guarantee.");

        public Task DeleteRowAsync(InnerDatabaseTarget target, string database, string table, DeleteRowRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Row delete is not exposed for ClickHouse - ALTER DELETE is asynchronous and lacks a single-row guarantee.");

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

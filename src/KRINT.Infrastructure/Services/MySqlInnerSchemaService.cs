using System.Globalization;
using KRINT.Infrastructure.Interfaces;
using MySqlConnector;

namespace KRINT.Infrastructure.Services
{
    public class MySqlInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "mysql";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var cmd = new MySqlCommand(
                "SELECT table_name, table_type FROM information_schema.tables WHERE table_schema = @schema ORDER BY table_name",
                conn);
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

            await using var cmd = new MySqlCommand(
                $"SELECT * FROM `{table}` LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}",
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

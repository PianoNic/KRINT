using System.Globalization;
using KRINT.Infrastructure.Interfaces;
using Npgsql;

namespace KRINT.Infrastructure.Services
{
    public class PostgresInnerSchemaService : IInnerSchemaService
    {
        public string Engine => "postgres";

        public async Task<IReadOnlyList<TableSummary>> ListTablesAsync(InnerDatabaseTarget target, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            await using var conn = await OpenAsync(target, database, cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "SELECT table_name, table_type FROM information_schema.tables " +
                "WHERE table_schema NOT IN ('pg_catalog','information_schema') " +
                "ORDER BY table_schema, table_name",
                conn);
            var results = new List<TableSummary>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                var kind = reader.GetString(1) == "VIEW" ? "view" : "table";
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
            await using (var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM \"{table}\"", conn))
            {
                var raw = await countCmd.ExecuteScalarAsync(cancellationToken);
                if (raw is long l) total = l;
            }

            await using var cmd = new NpgsqlCommand(
                $"SELECT * FROM \"{table}\" LIMIT {limit.ToString(CultureInfo.InvariantCulture)} OFFSET {offset.ToString(CultureInfo.InvariantCulture)}",
                conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            return await ReadRowsAsync(reader, total, cancellationToken);
        }

        private static async Task<TableRows> ReadRowsAsync(NpgsqlDataReader reader, long? total, CancellationToken cancellationToken)
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
            return new TableRows(columns, rows, total);
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
            };
            var conn = new NpgsqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}

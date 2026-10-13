using System.Data.Common;
using System.Diagnostics;
using ClickHouse.Client.ADO;
using KRINT.Infrastructure.Interfaces;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace KRINT.Infrastructure.Services
{
    // Shared materialiser. Every ADO.NET DbCommand produces a DbDataReader; we drain it into
    // string-projected rows so the SPA can render columns of any type without engine-specific
    // serialisation. NULLs come back as null (not "NULL"); binary blobs render as their string
    // form via Convert.ToString(value).
    internal static class QueryMaterialiser
    {
        public static async Task<QueryResult> ExecuteAsync(DbCommand cmd, int rowLimit, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var truncated = false;
            try
            {
                // FieldCount == 0 means the statement didn't produce a result set (INSERT/UPDATE/DDL).
                // Surface RecordsAffected so the user sees "3 rows affected" instead of an empty table.
                if (reader.FieldCount == 0)
                {
                    sw.Stop();
                    return new QueryResult(Array.Empty<QueryResultColumn>(), Array.Empty<IReadOnlyList<string?>>(), reader.RecordsAffected < 0 ? 0 : reader.RecordsAffected, sw.ElapsedMilliseconds, false);
                }

                var columns = new QueryResultColumn[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    columns[i] = new QueryResultColumn(reader.GetName(i), reader.GetFieldType(i)?.Name ?? "unknown");

                var rows = new List<IReadOnlyList<string?>>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (rows.Count >= rowLimit) { truncated = true; break; }
                    var row = new string?[reader.FieldCount];
                    for (var i = 0; i < reader.FieldCount; i++)
                        row[i] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
                    rows.Add(row);
                }

                var recordsAffected = reader.RecordsAffected < 0 ? rows.Count : reader.RecordsAffected;
                sw.Stop();
                return new QueryResult(columns, rows, recordsAffected, sw.ElapsedMilliseconds, truncated);
            }
            finally
            {
                if (truncated)
                {
                    // Disposing a reader drains whatever the server still sends, so a SELECT over a
                    // big table would stream the rest of it just to throw it away. Cancel the
                    // statement first; the drain then ends almost immediately.
                    try { cmd.Cancel(); } catch { /* not every driver supports it; drain instead */ }
                    try { await reader.DisposeAsync(); } catch { /* the cancellation error surfacing on close */ }
                }
                else
                {
                    await reader.DisposeAsync();
                }
            }
        }
    }

    public class PostgresInnerQueryService : IInnerQueryService
    {
        public virtual string Engine => "postgres";

        public async Task<QueryResult> RunAsync(InnerDatabaseTarget target, string database, string sql, int rowLimit, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = target.Host, Port = target.Port, Username = target.Username,
                Password = target.Password, Database = database, Timeout = 5, Pooling = false,
                CommandTimeout = 30,
            };
            await using var conn = new NpgsqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(sql, conn);
            return await QueryMaterialiser.ExecuteAsync(cmd, rowLimit, cancellationToken);
        }
    }

    public sealed class TimescaleDbInnerQueryService : PostgresInnerQueryService { public override string Engine => "timescaledb"; }
    public sealed class PgVectorInnerQueryService    : PostgresInnerQueryService { public override string Engine => "pgvector"; }

    public class CockroachDbInnerQueryService : IInnerQueryService
    {
        public string Engine => "cockroachdb";
        public async Task<QueryResult> RunAsync(InnerDatabaseTarget target, string database, string sql, int rowLimit, CancellationToken cancellationToken = default)
        {
            // Cockroach speaks the Postgres wire protocol but ships a fresh certificate per cluster.
            // Insecure-mode is what we provision with, so SslMode=Disable is correct here.
            InnerDatabaseNameValidator.Require(database);
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = target.Host, Port = target.Port, Username = target.Username,
                Database = database, Timeout = 5, Pooling = false, CommandTimeout = 30,
                SslMode = SslMode.Disable,
            };
            await using var conn = new NpgsqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(sql, conn);
            return await QueryMaterialiser.ExecuteAsync(cmd, rowLimit, cancellationToken);
        }
    }

    public class MySqlInnerQueryService : IInnerQueryService
    {
        public virtual string Engine => "mysql";
        public async Task<QueryResult> RunAsync(InnerDatabaseTarget target, string database, string sql, int rowLimit, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var csb = new MySqlConnectionStringBuilder
            {
                Server = target.Host, Port = (uint)target.Port, UserID = target.Username,
                Password = target.Password, Database = database, ConnectionTimeout = 5,
                // Pooled like the MySQL browse service; the connector resets session state on
                // return, and idle connections are closed after 30s so none linger on the instance.
                DefaultCommandTimeout = 30, Pooling = true, ConnectionIdleTimeout = 30,
            };
            await using var conn = new MySqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new MySqlCommand(sql, conn);
            return await QueryMaterialiser.ExecuteAsync(cmd, rowLimit, cancellationToken);
        }
    }

    public sealed class MariaDbInnerQueryService : MySqlInnerQueryService { public override string Engine => "mariadb"; }

    public class MsSqlInnerQueryService : IInnerQueryService
    {
        public string Engine => "mssql";
        public async Task<QueryResult> RunAsync(InnerDatabaseTarget target, string database, string sql, int rowLimit, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            var csb = new SqlConnectionStringBuilder
            {
                DataSource = $"{target.Host},{target.Port}", UserID = target.Username,
                Password = target.Password, InitialCatalog = database, ConnectTimeout = 5,
                TrustServerCertificate = true, Pooling = false, CommandTimeout = 30,
            };
            await using var conn = new SqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, conn);
            return await QueryMaterialiser.ExecuteAsync(cmd, rowLimit, cancellationToken);
        }
    }

    public class ClickHouseInnerQueryService : IInnerQueryService
    {
        public string Engine => "clickhouse";
        public async Task<QueryResult> RunAsync(InnerDatabaseTarget target, string database, string sql, int rowLimit, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(database);
            await using var conn = new ClickHouseConnection(BuildConnectionString(target, database));
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 30;
            return await QueryMaterialiser.ExecuteAsync(cmd, rowLimit, cancellationToken);
        }

        /// <summary>Built with the driver's builder rather than concatenated, so a password
        /// containing ';' or '=' cannot break the string or inject another setting.</summary>
        internal static string BuildConnectionString(InnerDatabaseTarget target, string database) =>
            new ClickHouseConnectionStringBuilder
            {
                Host = target.Host,
                Port = (ushort)target.Port,
                Username = target.Username,
                Password = target.Password,
                Database = database,
                Compression = false,
            }.ConnectionString;
    }
}

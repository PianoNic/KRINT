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
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

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
            var truncated = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count >= rowLimit) { truncated = true; break; }
                var row = new string?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
                rows.Add(row);
            }

            sw.Stop();
            return new QueryResult(columns, rows, reader.RecordsAffected < 0 ? rows.Count : reader.RecordsAffected, sw.ElapsedMilliseconds, truncated);
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
                DefaultCommandTimeout = 30, Pooling = false,
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
            // ClickHouse.Client takes a Postgres-style connection string; HTTP under the hood.
            var connStr = $"Host={target.Host};Port={target.Port};Username={target.Username};Password={target.Password};Database={database};Compress=false;";
            await using var conn = new ClickHouseConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 30;
            return await QueryMaterialiser.ExecuteAsync(cmd, rowLimit, cancellationToken);
        }
    }
}

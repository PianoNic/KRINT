using KRINT.Infrastructure.Interfaces;
using Npgsql;

namespace KRINT.Infrastructure.Services
{
    public class PostgresInnerDatabaseService : IInnerDatabaseService
    {
        public virtual string Engine => "postgres";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datistemplate = false AND datallowconn = true ORDER BY datname", conn);

            var results = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(reader.GetString(0));
            }
            return results;
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public virtual async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (string.Equals(name, target.DefaultDatabase, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to drop the instance's default database '{name}'.");
            }

            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand(BuildDropSql(name), conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Postgres-only: WITH (FORCE) terminates open sessions. CockroachDB doesn't support
        // the clause, so the subclass overrides this.
        protected virtual string BuildDropSql(string name) => $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";

        private static async Task<NpgsqlConnection> OpenAsync(InnerDatabaseTarget target, CancellationToken cancellationToken)
        {
            // Pooling=false: inner-DB admin calls are infrequent and short. A pool here only
            // collects broken connectors after a failure (e.g. auth retry) and then surfaces
            // them as ObjectDisposedException on the next call until the process restarts.
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = target.Host,
                Port = target.Port,
                Username = target.Username,
                Password = target.Password,
                Database = target.DefaultDatabase,
                Timeout = 5,
                Pooling = false,
            };
            var conn = new NpgsqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}

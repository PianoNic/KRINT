using KRINT.Infrastructure.Interfaces;
using Npgsql;

namespace KRINT.Infrastructure.Services
{
    public class PostgresInnerDatabaseService : IInnerDatabaseService
    {
        public string Engine => "postgres";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "SELECT datname FROM pg_database WHERE datistemplate = false AND datallowconn = true ORDER BY datname",
                conn);

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

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (string.Equals(name, target.DefaultDatabase, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to drop the instance's default database '{name}'.");
            }

            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<NpgsqlConnection> OpenAsync(InnerDatabaseTarget target, CancellationToken cancellationToken)
        {
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = target.Host,
                Port = target.Port,
                Username = target.Username,
                Password = target.Password,
                Database = target.DefaultDatabase,
                Timeout = 5,
            };
            var conn = new NpgsqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}

using KRINT.Infrastructure.Interfaces;
using Npgsql;

namespace KRINT.Infrastructure.Services
{
    public class PostgresInnerUserService : IInnerUserService
    {
        public string Engine => "postgres";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "SELECT rolname FROM pg_roles WHERE rolcanlogin = true AND rolname NOT LIKE 'pg\\_%' ORDER BY rolname",
                conn);

            var results = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) results.Add(reader.GetString(0));
            return results;
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            SafePasswordGuard.Require(password);
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand($"CREATE ROLE \"{name}\" LOGIN PASSWORD '{password}'", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (string.Equals(name, target.Username, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to drop the instance's superuser '{name}'.");
            }
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand($"DROP ROLE IF EXISTS \"{name}\"", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            SafePasswordGuard.Require(newPassword);
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand($"ALTER ROLE \"{name}\" WITH PASSWORD '{newPassword}'", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(user);
            InnerDatabaseNameValidator.Require(database);
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand($"GRANT ALL PRIVILEGES ON DATABASE \"{database}\" TO \"{user}\"", conn);
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
                Pooling = false,
            };
            var conn = new NpgsqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}

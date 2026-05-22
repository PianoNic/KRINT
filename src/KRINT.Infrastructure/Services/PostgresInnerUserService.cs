using KRINT.Infrastructure.Interfaces;
using Npgsql;

namespace KRINT.Infrastructure.Services
{
    public class PostgresInnerUserService : IInnerUserService
    {
        public virtual string Engine => "postgres";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT rolname FROM pg_roles WHERE rolcanlogin = true AND rolname NOT LIKE 'pg\\_%' ORDER BY rolname", conn);

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

            // DROP ROLE fails when the role owns objects or holds grants. REASSIGN OWNED moves
            // any owned objects to the superuser; DROP OWNED revokes remaining grants. These
            // statements are per-database, so we have to repeat them on every logical DB the
            // role might touch. Run them on connections opened TO each database in turn.
            await using var listConn = await OpenAsync(target, cancellationToken);
            var databases = new List<string>();
            await using (var listCmd = new NpgsqlCommand( "SELECT datname FROM pg_database WHERE datistemplate = false AND datallowconn = true", listConn))
            await using (var reader = await listCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken)) databases.Add(reader.GetString(0));
            }
            await listConn.CloseAsync();

            foreach (var db in databases)
            {
                await using var dbConn = await OpenAsync(target with { DefaultDatabase = db }, cancellationToken);
                await using var reassign = new NpgsqlCommand($"REASSIGN OWNED BY \"{name}\" TO \"{target.Username}\"; DROP OWNED BY \"{name}\"", dbConn);
                try { await reassign.ExecuteNonQueryAsync(cancellationToken); }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "42704") { /* role doesn't exist in this db's catalog */ }
            }

            await using var dropConn = await OpenAsync(target, cancellationToken);
            await using var dropCmd = new NpgsqlCommand($"DROP ROLE IF EXISTS \"{name}\"", dropConn);
            await dropCmd.ExecuteNonQueryAsync(cancellationToken);
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

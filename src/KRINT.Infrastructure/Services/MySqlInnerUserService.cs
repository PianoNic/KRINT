using KRINT.Infrastructure.Interfaces;
using MySqlConnector;

namespace KRINT.Infrastructure.Services
{
    public class MySqlInnerUserService : IInnerUserService
    {
        private static readonly HashSet<string> SystemUsers = new(StringComparer.OrdinalIgnoreCase)
        {
            "root", "mysql.sys", "mysql.session", "mysql.infoschema",
        };

        public string Engine => "mysql";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new MySqlCommand(
                "SELECT DISTINCT User FROM mysql.user ORDER BY User",
                conn);
            var results = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                if (!SystemUsers.Contains(name)) results.Add(name);
            }
            return results;
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            SafePasswordGuard.Require(password);
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new MySqlCommand($"CREATE USER `{name}`@'%' IDENTIFIED BY '{password}'", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DeleteAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (SystemUsers.Contains(name))
            {
                throw new InvalidOperationException($"Refusing to drop system user '{name}'.");
            }
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new MySqlCommand($"DROP USER IF EXISTS `{name}`@'%'", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            SafePasswordGuard.Require(newPassword);
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new MySqlCommand($"ALTER USER `{name}`@'%' IDENTIFIED BY '{newPassword}'", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task GrantAccessAsync(InnerDatabaseTarget target, string user, string database, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(user);
            InnerDatabaseNameValidator.Require(database);
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new MySqlCommand($"GRANT ALL PRIVILEGES ON `{database}`.* TO `{user}`@'%'", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<MySqlConnection> OpenAsync(InnerDatabaseTarget target, CancellationToken cancellationToken)
        {
            var csb = new MySqlConnectionStringBuilder
            {
                Server = target.Host,
                Port = (uint)target.Port,
                UserID = target.Username,
                Password = target.Password,
                ConnectionTimeout = 5,
            };
            var conn = new MySqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}

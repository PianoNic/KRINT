using KRINT.Infrastructure.Interfaces;
using MySqlConnector;

namespace KRINT.Infrastructure.Services
{
    public class MySqlInnerDatabaseService : IInnerDatabaseService
    {
        private static readonly HashSet<string> SystemSchemas = new(StringComparer.OrdinalIgnoreCase)
        {
            "mysql", "information_schema", "performance_schema", "sys",
        };

        public string Engine => "mysql";

        public async Task<IReadOnlyList<string>> ListAsync(InnerDatabaseTarget target, CancellationToken cancellationToken = default)
        {
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new MySqlCommand(
                "SELECT schema_name FROM information_schema.schemata ORDER BY schema_name",
                conn);

            var results = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                if (!SystemSchemas.Contains(name)) results.Add(name);
            }
            return results;
        }

        public async Task CreateAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new MySqlCommand($"CREATE DATABASE `{name}`", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DropAsync(InnerDatabaseTarget target, string name, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            if (SystemSchemas.Contains(name))
            {
                throw new InvalidOperationException($"Refusing to drop system schema '{name}'.");
            }

            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new MySqlCommand($"DROP DATABASE IF EXISTS `{name}`", conn);
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

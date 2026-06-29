using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KRINT.Infrastructure.Extensions
{
    public enum DatabaseProvider
    {
        Sqlite,
        Postgres
    }

    public static class DatabaseExtensions
    {
        // Postgres migrations live in KRINT.Infrastructure (the default assembly for the
        // DbContext). SQLite migrations live in their own assembly because EF Core cannot
        // hold two providers' migration sets and model snapshots in a single assembly.
        public const string SqliteMigrationsAssembly = "KRINT.Infrastructure.Migrations.Sqlite";

        private const string DefaultSqliteConnectionString = "Data Source=krint.db";

        public static DatabaseProvider GetDatabaseProvider(this IConfiguration configuration) =>
            ResolveProvider(configuration["Database:Provider"], configuration.GetConnectionString("KrintDatabase"));

        public static DatabaseProvider ParseProvider(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "sqlite" => DatabaseProvider.Sqlite,
            "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
            _ => throw new InvalidOperationException(
                $"Unknown Database:Provider '{value}'. Supported values are 'Sqlite' and 'Postgres'.")
        };

        /// <summary>
        /// Picks the provider. An explicit <c>Database:Provider</c> wins; when it's unset we infer
        /// from the connection string, so a Postgres string (e.g. <c>Host=db;…</c>) isn't fed to the
        /// SQLite provider - which otherwise crashes with "Connection string keyword 'host' is not
        /// supported". Falls back to SQLite when there's nothing to go on.
        /// </summary>
        public static DatabaseProvider ResolveProvider(string? providerValue, string? connectionString)
        {
            if (!string.IsNullOrWhiteSpace(providerValue))
                return ParseProvider(providerValue);

            var cs = (connectionString ?? string.Empty).ToLowerInvariant();
            var looksPostgres = cs.Contains("host=") || cs.Contains("server=")
                || cs.Contains("username=") || cs.Contains("user id=");
            return looksPostgres ? DatabaseProvider.Postgres : DatabaseProvider.Sqlite;
        }

        public static IServiceCollection AddKrintDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("KrintDatabase");
            var provider = ResolveProvider(configuration["Database:Provider"], connectionString);

            services.AddDbContext<KrintDbContext>(options => options.ConfigureKrintProvider(provider, connectionString));
            return services;
        }

        public static DbContextOptionsBuilder ConfigureKrintProvider(
            this DbContextOptionsBuilder options,
            DatabaseProvider provider,
            string? connectionString)
        {
            switch (provider)
            {
                case DatabaseProvider.Postgres:
                    options.UseNpgsql(
                        connectionString,
                        npgsql => npgsql.EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorCodesToAdd: null));
                    break;

                case DatabaseProvider.Sqlite:
                    options.UseSqlite(
                        string.IsNullOrWhiteSpace(connectionString) ? DefaultSqliteConnectionString : connectionString,
                        sqlite => sqlite.MigrationsAssembly(SqliteMigrationsAssembly));
                    break;
            }

            return options;
        }
    }
}

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
            ParseProvider(configuration["Database:Provider"]);

        public static DatabaseProvider ParseProvider(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "sqlite" => DatabaseProvider.Sqlite,
            "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
            _ => throw new InvalidOperationException(
                $"Unknown Database:Provider '{value}'. Supported values are 'Sqlite' and 'Postgres'.")
        };

        public static IServiceCollection AddKrintDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            var provider = configuration.GetDatabaseProvider();
            var connectionString = configuration.GetConnectionString("KrintDatabase");

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

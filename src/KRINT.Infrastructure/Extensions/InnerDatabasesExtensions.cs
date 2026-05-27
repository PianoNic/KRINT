using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KRINT.Infrastructure.Extensions
{
    public static class InnerDatabasesExtensions
    {
        public static IServiceCollection AddInnerDatabases(this IServiceCollection services)
        {
            services.AddSingleton<IInnerDatabaseService, PostgresInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, TimescaleDbInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, MySqlInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, MariaDbInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, MongoInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, RedisInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, CockroachDbInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, ClickHouseInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, CassandraInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, CouchDbInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, ElasticInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, PgVectorInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, Neo4jInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, QdrantInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, ValkeyInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, MsSqlInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseServiceResolver, InnerDatabaseServiceResolver>();

            services.AddSingleton<IInnerUserService, PostgresInnerUserService>();
            services.AddSingleton<IInnerUserService, TimescaleDbInnerUserService>();
            services.AddSingleton<IInnerUserService, MySqlInnerUserService>();
            services.AddSingleton<IInnerUserService, MariaDbInnerUserService>();
            services.AddSingleton<IInnerUserService, MongoInnerUserService>();
            services.AddSingleton<IInnerUserService, RedisInnerUserService>();
            services.AddSingleton<IInnerUserService, CockroachDbInnerUserService>();
            services.AddSingleton<IInnerUserService, ClickHouseInnerUserService>();
            services.AddSingleton<IInnerUserService, CassandraInnerUserService>();
            services.AddSingleton<IInnerUserService, CouchDbInnerUserService>();
            services.AddSingleton<IInnerUserService, ElasticInnerUserService>();
            services.AddSingleton<IInnerUserService, PgVectorInnerUserService>();
            services.AddSingleton<IInnerUserService, Neo4jInnerUserService>();
            services.AddSingleton<IInnerUserService, QdrantInnerUserService>();
            services.AddSingleton<IInnerUserService, ValkeyInnerUserService>();
            services.AddSingleton<IInnerUserService, MsSqlInnerUserService>();
            services.AddSingleton<IInnerUserServiceResolver, InnerUserServiceResolver>();

            services.AddSingleton<IInnerSchemaService, PostgresInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, TimescaleDbInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, MySqlInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, MariaDbInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, MongoInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, RedisInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, CockroachDbInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, ClickHouseInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, CassandraInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, CouchDbInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, ElasticInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, PgVectorInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, Neo4jInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, QdrantInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, ValkeyInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, MsSqlInnerSchemaService>();
            services.AddSingleton<IInnerSchemaServiceResolver, InnerSchemaServiceResolver>();

            // Query console - SQL engines only for v1. Engines without an entry surface in the
            // UI as "console not available" rather than throwing.
            services.AddSingleton<IInnerQueryService, PostgresInnerQueryService>();
            services.AddSingleton<IInnerQueryService, TimescaleDbInnerQueryService>();
            services.AddSingleton<IInnerQueryService, PgVectorInnerQueryService>();
            services.AddSingleton<IInnerQueryService, CockroachDbInnerQueryService>();
            services.AddSingleton<IInnerQueryService, MySqlInnerQueryService>();
            services.AddSingleton<IInnerQueryService, MariaDbInnerQueryService>();
            services.AddSingleton<IInnerQueryService, MsSqlInnerQueryService>();
            services.AddSingleton<IInnerQueryService, ClickHouseInnerQueryService>();
            services.AddSingleton<IInnerQueryServiceResolver, InnerQueryServiceResolver>();

            services.AddScoped<IBackupService, PostgresBackupService>();
            services.AddScoped<IBackupService, TimescaleDbBackupService>();
            services.AddScoped<IBackupService, MySqlBackupService>();
            services.AddScoped<IBackupService, MariaDbBackupService>();
            services.AddScoped<IBackupService, MongoBackupService>();
            services.AddScoped<IBackupService, RedisBackupService>();
            services.AddScoped<IBackupService, PgVectorBackupService>();
            services.AddScoped<IBackupService, ValkeyBackupService>();
            services.AddScoped<IBackupService, MsSqlBackupService>();
            services.AddScoped<IBackupServiceResolver, BackupServiceResolver>();
            return services;
        }
    }

    public interface IBackupServiceResolver
    {
        IBackupService Resolve(string engine);
    }

    internal sealed class BackupServiceResolver(IEnumerable<IBackupService> services) : IBackupServiceResolver
    {
        private readonly Dictionary<string, IBackupService> _byEngine =
            services.ToDictionary(s => s.Engine, StringComparer.OrdinalIgnoreCase);

        public IBackupService Resolve(string engine) =>
            _byEngine.TryGetValue(engine, out var svc)
                ? svc
                : throw new NotSupportedException($"No backup service registered for engine '{engine}'.");
    }

    public interface IInnerSchemaServiceResolver
    {
        IInnerSchemaService Resolve(string engine);
    }

    internal sealed class InnerSchemaServiceResolver(IEnumerable<IInnerSchemaService> services) : IInnerSchemaServiceResolver
    {
        private readonly Dictionary<string, IInnerSchemaService> _byEngine =
            services.ToDictionary(s => s.Engine, StringComparer.OrdinalIgnoreCase);

        public IInnerSchemaService Resolve(string engine) =>
            _byEngine.TryGetValue(engine, out var svc)
                ? svc
                : throw new NotSupportedException($"No inner-schema service registered for engine '{engine}'.");
    }

    public interface IInnerDatabaseServiceResolver
    {
        IInnerDatabaseService Resolve(string engine);
    }

    internal sealed class InnerDatabaseServiceResolver(IEnumerable<IInnerDatabaseService> services) : IInnerDatabaseServiceResolver
    {
        private readonly Dictionary<string, IInnerDatabaseService> _byEngine =
            services.ToDictionary(s => s.Engine, StringComparer.OrdinalIgnoreCase);

        public IInnerDatabaseService Resolve(string engine) =>
            _byEngine.TryGetValue(engine, out var svc)
                ? svc
                : throw new NotSupportedException($"No inner-database service registered for engine '{engine}'.");
    }

    public interface IInnerQueryServiceResolver
    {
        IInnerQueryService? TryResolve(string engine);
        bool IsSupported(string engine);
    }

    internal sealed class InnerQueryServiceResolver(IEnumerable<IInnerQueryService> services) : IInnerQueryServiceResolver
    {
        private readonly Dictionary<string, IInnerQueryService> _byEngine =
            services.ToDictionary(s => s.Engine, StringComparer.OrdinalIgnoreCase);

        // Soft resolve: query console is opt-in per engine. The API returns 400 with a clear
        // message when the engine has no console driver, rather than 500-ing.
        public IInnerQueryService? TryResolve(string engine) =>
            _byEngine.TryGetValue(engine, out var svc) ? svc : null;
        public bool IsSupported(string engine) => _byEngine.ContainsKey(engine);
    }

    public interface IInnerUserServiceResolver
    {
        IInnerUserService Resolve(string engine);
    }

    internal sealed class InnerUserServiceResolver(IEnumerable<IInnerUserService> services) : IInnerUserServiceResolver
    {
        private readonly Dictionary<string, IInnerUserService> _byEngine =
            services.ToDictionary(s => s.Engine, StringComparer.OrdinalIgnoreCase);

        public IInnerUserService Resolve(string engine) =>
            _byEngine.TryGetValue(engine, out var svc)
                ? svc
                : throw new NotSupportedException($"No inner-user service registered for engine '{engine}'.");
    }
}

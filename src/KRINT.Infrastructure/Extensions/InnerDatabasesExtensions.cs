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
            services.AddSingleton<IInnerDatabaseService, MySqlInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, MariaDbInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseService, MongoInnerDatabaseService>();
            services.AddSingleton<IInnerDatabaseServiceResolver, InnerDatabaseServiceResolver>();

            services.AddSingleton<IInnerUserService, PostgresInnerUserService>();
            services.AddSingleton<IInnerUserService, MySqlInnerUserService>();
            services.AddSingleton<IInnerUserService, MariaDbInnerUserService>();
            services.AddSingleton<IInnerUserService, MongoInnerUserService>();
            services.AddSingleton<IInnerUserServiceResolver, InnerUserServiceResolver>();

            services.AddSingleton<IInnerSchemaService, PostgresInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, MySqlInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, MariaDbInnerSchemaService>();
            services.AddSingleton<IInnerSchemaService, MongoInnerSchemaService>();
            services.AddSingleton<IInnerSchemaServiceResolver, InnerSchemaServiceResolver>();

            services.AddScoped<IBackupService, PostgresBackupService>();
            services.AddScoped<IBackupService, MySqlBackupService>();
            services.AddScoped<IBackupService, MariaDbBackupService>();
            services.AddScoped<IBackupService, MongoBackupService>();
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

using Microsoft.EntityFrameworkCore;
using Toamaisutaa.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using KRINT.Domain;
using KRINT.Infrastructure.Extensions;

namespace KRINT.Infrastructure
{
    public class KrintDbContext(DbContextOptions<KrintDbContext> options) : DbContext(options)
    {
        public DbSet<Secret> Secrets => Set<Secret>();
        public DbSet<DatabaseInstance> DatabaseInstances => Set<DatabaseInstance>();
        public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();
        public DbSet<BackupEntry> BackupEntries => Set<BackupEntry>();
        public DbSet<BackupSchedule> BackupSchedules => Set<BackupSchedule>();
        public DbSet<Node> Nodes => Set<Node>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(KrintDbContext).Assembly);
            // Local login users, credentials and refresh tokens live next to KRINT's own tables.
            // Mapped unconditionally so both migration sets carry them; the tables stay empty on a
            // deployment that authenticates against an identity provider.
            modelBuilder.ApplyToamaisutaaConfiguration();
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            // Every timestamp KRINT stores is UTC, but SQLite (and Postgres "timestamp" reads in some
            // paths) hand DateTimes back as Unspecified, which serialize without the trailing "Z" and
            // get parsed as local time by the browser. Stamp them Utc on the way in and out.
            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        }

        private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
            v => ToUtc(v),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? ToUtc(v.Value) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        private static DateTime ToUtc(DateTime value) =>
            value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplySaveChangesGuards();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ApplySaveChangesGuards();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplySaveChangesGuards()
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
    }

    public class KrintDbContextFactory : IDesignTimeDbContextFactory<KrintDbContext>
    {
        public KrintDbContext CreateDbContext(string[] args)
        {
            // Used by the EF Core CLI. Provider and connection string are read straight from
            // the environment so `dotnet ef` targets the same database the app would at runtime.
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__KrintDatabase");
            var provider = DatabaseExtensions.ResolveProvider(Environment.GetEnvironmentVariable("Database__Provider"), connectionString);

            var optionsBuilder = new DbContextOptionsBuilder<KrintDbContext>();
            optionsBuilder.ConfigureKrintProvider(provider, connectionString);
            return new KrintDbContext(optionsBuilder.Options);
        }
    }
}

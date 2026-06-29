using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
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
        }

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

using KRINT.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KRINT.Infrastructure.DBConfigurations
{
    public class BackupEntryConfiguration : IEntityTypeConfiguration<BackupEntry>
    {
        public void Configure(EntityTypeBuilder<BackupEntry> builder)
        {
            // Backups are listed and pruned per instance.
            builder.HasIndex(b => b.InstanceId);
        }
    }
}

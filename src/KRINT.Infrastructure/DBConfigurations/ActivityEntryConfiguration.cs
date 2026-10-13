using KRINT.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KRINT.Infrastructure.DBConfigurations
{
    public class ActivityEntryConfiguration : IEntityTypeConfiguration<ActivityEntry>
    {
        public void Configure(EntityTypeBuilder<ActivityEntry> builder)
        {
            // The activity feed is always read newest-first.
            builder.HasIndex(a => a.CreatedAt);
        }
    }
}

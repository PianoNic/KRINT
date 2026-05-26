using KRINT.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KRINT.Infrastructure.DBConfigurations
{
    public class DatabaseInstanceConfiguration : IEntityTypeConfiguration<DatabaseInstance>
    {
        public void Configure(EntityTypeBuilder<DatabaseInstance> builder)
        {
            // Externally-registered instances have no container - filter the unique index so
            // those nullable rows don't collide with each other.
            builder.HasIndex(d => d.ContainerName)
                .IsUnique()
                .HasFilter("\"ContainerName\" IS NOT NULL");
        }
    }
}

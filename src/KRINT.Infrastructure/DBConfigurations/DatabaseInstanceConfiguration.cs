using KRINT.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KRINT.Infrastructure.DBConfigurations
{
    public class DatabaseInstanceConfiguration : IEntityTypeConfiguration<DatabaseInstance>
    {
        public void Configure(EntityTypeBuilder<DatabaseInstance> builder)
        {
            builder.HasIndex(d => d.ContainerName).IsUnique();
        }
    }
}

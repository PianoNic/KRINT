using KRINT.Application.Dtos.Database;

namespace KRINT.Application.Mappings.Database
{
    public static class ProvisionedDatabaseMappings
    {
        public static ProvisionedDatabaseDto ToProvisionedDto(this KRINT.Domain.DatabaseInstance instance, string password, string connectionString) => new()
        {
            Id = instance.Id,
            Engine = instance.Engine,
            Version = instance.Version,
            ContainerName = instance.ContainerName,
            Host = instance.Host,
            Port = instance.Port,
            Username = instance.Username,
            DatabaseName = instance.DatabaseName,
            Password = password,
            ConnectionString = connectionString,
            CreatedAt = instance.CreatedAt,
            IsManaged = instance.IsManaged,
            IsPublic = instance.IsPublic,
        };
    }
}

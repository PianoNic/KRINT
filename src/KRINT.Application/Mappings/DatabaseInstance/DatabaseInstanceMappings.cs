using KRINT.Application.Dtos.DatabaseInstance;

namespace KRINT.Application.Mappings.DatabaseInstance
{
    public static class DatabaseInstanceMappings
    {
        public static DatabaseInstanceDto ToDto(this KRINT.Domain.DatabaseInstance d) => new()
        {
            Id = d.Id,
            Engine = d.Engine,
            Version = d.Version,
            PreviousVersion = d.PreviousVersion,
            DisplayName = d.DisplayName,
            ContainerName = d.ContainerName,
            Host = d.Host,
            Port = d.Port,
            Username = d.Username,
            DatabaseName = d.DatabaseName,
            CreatedAt = d.CreatedAt,
        };
    }
}

namespace KRINT.Application.Dtos.Database
{
    public record ProvisionedDatabaseDto
    {
        public required Guid Id { get; init; }
        public required string Engine { get; init; }
        public required string Version { get; init; }
        /// <summary>Null for externally-registered instances.</summary>
        public string? ContainerName { get; init; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public required string Username { get; init; }
        public required string DatabaseName { get; init; }
        public required string Password { get; init; }
        public required string ConnectionString { get; init; }
        public required DateTime CreatedAt { get; init; }
        public required bool IsManaged { get; init; }
        public required bool IsPublic { get; init; }
        public string? State { get; init; }
        public required bool IsConfigManaged { get; init; }
    }
}

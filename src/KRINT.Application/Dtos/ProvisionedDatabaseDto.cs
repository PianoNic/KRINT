namespace KRINT.Application.Dtos
{
    public record ProvisionedDatabaseDto
    {
        public required Guid Id { get; init; }
        public required string Engine { get; init; }
        public required string Version { get; init; }
        public required string ContainerName { get; init; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public required string Username { get; init; }
        public required string DatabaseName { get; init; }
        public required string Password { get; init; }
        public required string ConnectionString { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}

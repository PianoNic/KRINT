namespace KRINT.Application.Dtos.DatabaseInstance
{
    public record DatabaseInstanceDto
    {
        public required Guid Id { get; init; }
        public required string Engine { get; init; }
        public required string Version { get; init; }
        /// <summary>User-picked human-readable name. Mutable via PATCH /api/Database/{id}.</summary>
        public required string DisplayName { get; init; }
        public required string ContainerName { get; init; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public required string Username { get; init; }
        public required string DatabaseName { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}

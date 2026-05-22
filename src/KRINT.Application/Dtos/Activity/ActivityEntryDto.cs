namespace KRINT.Application.Dtos.Activity
{
    public record ActivityEntryDto
    {
        public required Guid Id { get; init; }
        public required string Action { get; init; }
        public required string Target { get; init; }
        public Guid? InstanceId { get; init; }
        public string? Engine { get; init; }
        public string? Details { get; init; }
        public string? ActorName { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}

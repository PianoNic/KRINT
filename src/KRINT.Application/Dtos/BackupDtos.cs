namespace KRINT.Application.Dtos
{
    public record BackupEntryDto
    {
        public required Guid Id { get; init; }
        public required Guid InstanceId { get; init; }
        public required string Engine { get; init; }
        public required string FileName { get; init; }
        public required long SizeBytes { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}

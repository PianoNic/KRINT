namespace KRINT.Application.Dtos.BackupSchedule
{
    public record BackupScheduleDto
    {
        public required Guid Id { get; init; }
        public required Guid InstanceId { get; init; }
        public required string CronExpression { get; init; }
        public required string Description { get; init; }
        public required bool Enabled { get; init; }
        public DateTime? LastRunAt { get; init; }
        public string? LastStatus { get; init; }
        public string? LastError { get; init; }
        public DateTime? NextRunAt { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}

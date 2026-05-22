namespace KRINT.Application.Dtos.BackupSchedule
{
    public record CreateBackupScheduleDto
    {
        public required Guid InstanceId { get; init; }
        public required string CronExpression { get; init; }
        public required string Description { get; init; }
    }
}

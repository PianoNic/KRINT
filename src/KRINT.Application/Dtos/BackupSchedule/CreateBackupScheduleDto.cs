namespace KRINT.Application.Dtos.BackupSchedule
{
    public record CreateBackupScheduleDto
    {
        public required Guid InstanceId { get; init; }
        public required string CronExpression { get; init; }
        public required string Description { get; init; }
        /// <summary>Keep only this many newest backups of the instance after each scheduled run.
        /// Null keeps everything.</summary>
        public int? KeepLast { get; init; }
    }
}

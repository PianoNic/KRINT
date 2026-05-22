namespace KRINT.Domain
{
    public class BackupSchedule : BaseEntity
    {
        public required Guid InstanceId { get; init; }
        public required string CronExpression { get; set; }
        public required string Description { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTime? LastRunAt { get; set; }
        public string? LastStatus { get; set; }
        public string? LastError { get; set; }
        public DateTime? NextRunAt { get; set; }
    }
}

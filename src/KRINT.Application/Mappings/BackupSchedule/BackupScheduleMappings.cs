using KRINT.Application.Dtos.BackupSchedule;
using KRINT.Domain;

namespace KRINT.Application.Mappings.BackupSchedule
{
    public static class BackupScheduleMappings
    {
        public static BackupScheduleDto ToDto(this KRINT.Domain.BackupSchedule s) => new()
        {
            Id = s.Id,
            InstanceId = s.InstanceId,
            CronExpression = s.CronExpression,
            Description = s.Description,
            Enabled = s.Enabled,
            LastRunAt = s.LastRunAt,
            LastStatus = s.LastStatus,
            LastError = s.LastError,
            NextRunAt = s.NextRunAt,
            CreatedAt = s.CreatedAt,
        };
    }
}

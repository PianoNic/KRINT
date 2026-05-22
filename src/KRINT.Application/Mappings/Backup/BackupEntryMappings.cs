using KRINT.Application.Dtos.Backup;
using KRINT.Domain;

namespace KRINT.Application.Mappings.Backup
{
    public static class BackupEntryMappings
    {
        public static BackupEntryDto ToDto(this BackupEntry b) => new()
        {
            Id = b.Id,
            InstanceId = b.InstanceId,
            Engine = b.Engine,
            FileName = b.FileName,
            SizeBytes = b.SizeBytes,
            CreatedAt = b.CreatedAt,
        };
    }
}

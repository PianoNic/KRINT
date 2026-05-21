namespace KRINT.Domain
{
    public class BackupEntry : BaseEntity
    {
        public required Guid InstanceId { get; init; }
        public required string Engine { get; init; }
        public required string FileName { get; init; }
        public required string FilePath { get; init; }
        public required long SizeBytes { get; init; }
    }
}

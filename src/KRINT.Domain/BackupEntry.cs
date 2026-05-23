namespace KRINT.Domain
{
    public class BackupEntry : BaseEntity
    {
        public required Guid InstanceId { get; init; }
        public required string Engine { get; init; }
        /// <summary>The instance's Version (e.g. "18.4") captured when this backup was created
        /// or imported. Surfaced in the UI so the user can pair a dump with the right target
        /// version on restore. Default empty for backups taken before this field existed.</summary>
        public string EngineVersion { get; init; } = string.Empty;
        public required string FileName { get; init; }
        public required string FilePath { get; init; }
        public required long SizeBytes { get; init; }
    }
}

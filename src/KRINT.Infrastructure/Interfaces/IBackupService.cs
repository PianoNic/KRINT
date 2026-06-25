namespace KRINT.Infrastructure.Interfaces
{
    public record BackupTarget(string ContainerId, string ContainerName, string Engine, string Username, string Password, string DefaultDatabase, Guid? NodeId = null);

    public record BackupOutput(byte[] Content, string FileExtension);

    public interface IBackupService
    {
        string Engine { get; }

        Task<BackupOutput> DumpAsync(BackupTarget target, CancellationToken cancellationToken = default);

        Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default);
    }
}

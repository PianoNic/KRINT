using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public class MongoBackupService(IDockerServiceResolver dockerResolver) : IBackupService
    {
        public string Engine => "mongo";

        public async Task<BackupOutput> DumpAsync(BackupTarget target, CancellationToken cancellationToken = default)
        {
            // mongodump --archive sends a single binary archive to stdout.
            var cmd = new List<string>
            {
                "bash", "-c",
                $"mongodump --host 127.0.0.1 --username {target.Username} --password '{target.Password}' --authenticationDatabase admin --archive",
            };
            var bytes = await dockerResolver.Resolve(target.NodeId).ExecCaptureAsync(target.ContainerId, cmd, cancellationToken);
            return new BackupOutput(bytes, "archive");
        }

        public async Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default)
        {
            // mongorestore --archive reads a binary archive from stdin; --drop replaces existing collections.
            var cmd = new List<string>
            {
                "bash", "-c",
                $"mongorestore --host 127.0.0.1 --username {target.Username} --password '{target.Password}' --authenticationDatabase admin --archive --drop",
            };
            await dockerResolver.Resolve(target.NodeId).ExecWithStdinAsync(target.ContainerId, cmd, dump, cancellationToken);
        }
    }
}

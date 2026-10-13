using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public class MongoBackupService(IDockerServiceResolver dockerResolver) : IBackupService
    {
        public string Engine => "mongo";

        public string FileExtension => "archive";

        public Task DumpToAsync(BackupTarget target, Stream output, CancellationToken cancellationToken = default)
        {
            // mongodump --archive sends a single binary archive to stdout.
            // Credentials as argv ($1, $2) so the shell never parses them.
            var cmd = new List<string>
            {
                "bash", "-c",
                "mongodump --host 127.0.0.1 --username \"$2\" --password \"$1\" --authenticationDatabase admin --archive",
                "krint", target.Password, target.Username,
            };
            return dockerResolver.Resolve(target.NodeId).ExecToStreamAsync(target.ContainerId, cmd, output, target.Timeout, cancellationToken);
        }

        public Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default)
        {
            // mongorestore --archive reads a binary archive from stdin; --drop replaces existing collections.
            var cmd = new List<string>
            {
                "bash", "-c",
                "mongorestore --host 127.0.0.1 --username \"$2\" --password \"$1\" --authenticationDatabase admin --archive --drop",
                "krint", target.Password, target.Username,
            };
            return dockerResolver.Resolve(target.NodeId).ExecWithStdinAsync(target.ContainerId, cmd, dump, target.Timeout, cancellationToken);
        }
    }
}

using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public class PostgresBackupService(IDockerService docker) : IBackupService
    {
        public string Engine => "postgres";

        public async Task<BackupOutput> DumpAsync(BackupTarget target, CancellationToken cancellationToken = default)
        {
            // pg_dump custom-format archive — restorable via pg_restore. Sent over stdout.
            var cmd = new List<string>
            {
                "bash", "-c",
                $"PGPASSWORD='{target.Password}' pg_dump -h 127.0.0.1 -U {target.Username} -d {target.DefaultDatabase} -F c",
            };
            var bytes = await docker.ExecCaptureAsync(target.ContainerId, cmd, cancellationToken);
            return new BackupOutput(bytes, "dump");
        }

        public async Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default)
        {
            // pg_restore reads a custom-format archive from stdin. --clean --if-exists drops
            // existing objects first so re-applying a dump is idempotent. --no-owner skips role
            // re-assignment that would fail if the original owner doesn't exist on this server.
            var cmd = new List<string>
            {
                "bash", "-c",
                $"PGPASSWORD='{target.Password}' pg_restore -h 127.0.0.1 -U {target.Username} -d {target.DefaultDatabase} --clean --if-exists --no-owner",
            };
            await docker.ExecWithStdinAsync(target.ContainerId, cmd, dump, cancellationToken);
        }
    }
}

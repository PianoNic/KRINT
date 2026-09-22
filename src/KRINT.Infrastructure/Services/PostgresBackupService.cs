using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public class PostgresBackupService(IDockerServiceResolver dockerResolver) : IBackupService
    {
        protected IDockerService Docker(BackupTarget target) => dockerResolver.Resolve(target.NodeId);

        public virtual string Engine => "postgres";

        public async Task<BackupOutput> DumpAsync(BackupTarget target, CancellationToken cancellationToken = default)
        {
            // pg_dump custom-format archive - restorable via pg_restore. Sent over stdout.
            // Credentials and names travel as argv ($1..$3), so the shell never parses them:
            // an adopted external's password is whatever the user typed.
            var cmd = new List<string>
            {
                "bash", "-c",
                "PGPASSWORD=\"$1\" pg_dump -h 127.0.0.1 -U \"$2\" -d \"$3\" -F c",
                "krint", target.Password, target.Username, target.DefaultDatabase,
            };
            var bytes = await Docker(target).ExecCaptureAsync(target.ContainerId, cmd, cancellationToken);
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
                "PGPASSWORD=\"$1\" pg_restore -h 127.0.0.1 -U \"$2\" -d \"$3\" --clean --if-exists --no-owner",
                "krint", target.Password, target.Username, target.DefaultDatabase,
            };
            await Docker(target).ExecWithStdinAsync(target.ContainerId, cmd, dump, cancellationToken);
        }
    }
}

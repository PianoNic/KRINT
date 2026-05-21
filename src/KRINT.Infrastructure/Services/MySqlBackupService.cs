using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public class MySqlBackupService(IDockerService docker) : IBackupService
    {
        public string Engine => "mysql";

        public async Task<BackupOutput> DumpAsync(BackupTarget target, CancellationToken cancellationToken = default)
        {
            // mysqldump --all-databases, consistent snapshot via single-transaction.
            var cmd = new List<string>
            {
                "bash", "-c",
                $"mysqldump -h 127.0.0.1 -u {target.Username} -p'{target.Password}' --single-transaction --all-databases",
            };
            var bytes = await docker.ExecCaptureAsync(target.ContainerId, cmd, cancellationToken);
            return new BackupOutput(bytes, "sql");
        }

        public async Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default)
        {
            // Pipe the SQL dump into `mysql` over stdin — it replays statements from --all-databases.
            var cmd = new List<string>
            {
                "bash", "-c",
                $"mysql -h 127.0.0.1 -u {target.Username} -p'{target.Password}'",
            };
            await docker.ExecWithStdinAsync(target.ContainerId, cmd, dump, cancellationToken);
        }
    }
}

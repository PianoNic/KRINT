using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    public class MySqlBackupService(IDockerServiceResolver dockerResolver) : IBackupService
    {
        public virtual string Engine => "mysql";

        // Dump/restore client binaries. MySQL ships `mysqldump`/`mysql`; MariaDB 11+ ships
        // `mariadb-dump`/`mariadb` and dropped the mysql* names. Resolved at runtime with a
        // fallback so both old and new images work; subclasses set the preferred name.
        protected virtual string DumpBinary => "mysqldump";
        protected virtual string DumpFallback => "mariadb-dump";
        protected virtual string RestoreBinary => "mysql";
        protected virtual string RestoreFallback => "mariadb";

        public async Task<BackupOutput> DumpAsync(BackupTarget target, CancellationToken cancellationToken = default)
        {
            // dump --all-databases, consistent snapshot via single-transaction. Pick whichever
            // client binary the image actually has.
            var bin = $"$(command -v {DumpBinary} || command -v {DumpFallback})";
            var cmd = new List<string>
            {
                "bash", "-c",
                $"{bin} -h 127.0.0.1 -u {target.Username} -p'{target.Password}' --single-transaction --all-databases",
            };
            var bytes = await dockerResolver.Resolve(target.NodeId).ExecCaptureAsync(target.ContainerId, cmd, cancellationToken);
            return new BackupOutput(bytes, "sql");
        }

        public async Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default)
        {
            // Pipe the SQL dump into the client over stdin - it replays statements from --all-databases.
            var bin = $"$(command -v {RestoreBinary} || command -v {RestoreFallback})";
            var cmd = new List<string>
            {
                "bash", "-c",
                $"{bin} -h 127.0.0.1 -u {target.Username} -p'{target.Password}'",
            };
            await dockerResolver.Resolve(target.NodeId).ExecWithStdinAsync(target.ContainerId, cmd, dump, cancellationToken);
        }
    }
}

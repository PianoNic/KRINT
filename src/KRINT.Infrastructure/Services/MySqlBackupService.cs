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

        // MySQL 8.4+ writes SET @@GLOBAL.GTID_PURGED into every dump, and replaying that into a
        // server that already has executed GTIDs (the same instance, on restore) fails with
        // ERROR 3546. The dump is a logical copy of the data, not a replication seed, so the
        // GTID bookkeeping is left out. mariadb-dump does not know the flag, so MariaDB clears it.
        protected virtual string DumpExtraArgs => "--set-gtid-purged=OFF";

        public async Task<BackupOutput> DumpAsync(BackupTarget target, CancellationToken cancellationToken = default)
        {
            // dump --all-databases, consistent snapshot via single-transaction. Pick whichever
            // client binary the image actually has.
            var bin = $"$(command -v {DumpBinary} || command -v {DumpFallback})";
            // Credentials as argv ($1, $2): the shell never parses them, so an adopted external's
            // typed password cannot break out of the script.
            var cmd = new List<string>
            {
                "bash", "-c",
                $"MYSQL_PWD=\"$1\" {bin} -h 127.0.0.1 -u \"$2\" --single-transaction --all-databases {DumpExtraArgs}".TrimEnd(),
                "krint", target.Password, target.Username,
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
                $"MYSQL_PWD=\"$1\" {bin} -h 127.0.0.1 -u \"$2\"",
                "krint", target.Password, target.Username,
            };
            await dockerResolver.Resolve(target.NodeId).ExecWithStdinAsync(target.ContainerId, cmd, dump, cancellationToken);
        }
    }
}

using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// MSSQL backup via native BACKUP / RESTORE. The Microsoft image ships sqlcmd at
    /// /opt/mssql-tools18/bin/sqlcmd; we shell into the container so the KRINT host
    /// doesn't need MSSQL tooling installed.
    ///
    /// BACKUP / RESTORE write to a binary .bak file rather than emitting SQL to stdout, so the
    /// flow has an extra hop: BACKUP into /tmp inside the container, then cat the file to
    /// stdout (for dump) or cat stdin to /tmp then RESTORE (for restore). The .bak is removed
    /// either way.
    ///
    /// Caveats this implementation accepts:
    ///   - sqlcmd must live at the mssql-tools18 path. Containers based on older SQL Server
    ///     images (where it was /opt/mssql-tools/bin/sqlcmd) will fail with a clear error.
    ///   - RESTORE does *not* MOVE the data/log files - it relies on the source's logical
    ///     filenames lining up with the target. This holds for the two flows that call this
    ///     service today: the migration flow (target's DefaultDatabase = source's
    ///     DatabaseName) and the upgrade flow (same instance, just a newer engine binary).
    ///     Restoring an arbitrary .bak into a renamed database would need RESTORE FILELISTONLY +
    ///     dynamic MOVE clauses, which is out of scope for v1.
    /// </summary>
    public sealed class MsSqlBackupService(IDockerServiceResolver dockerResolver) : IBackupService
    {
        public string Engine => "mssql";

        // SQL Server 2022 official image. -N enables encryption (required by the v18 driver);
        // -C trusts the self-signed cert the container generates on first boot.
        private const string SqlcmdPath = "/opt/mssql-tools18/bin/sqlcmd";
        private const string ToolsArgs = "-N -C";

        public async Task<BackupOutput> DumpAsync(BackupTarget target, CancellationToken cancellationToken = default)
        {
            var tmp = $"/tmp/krint-{Guid.NewGuid():N}.bak";
            // INIT overwrites whatever lives at the path (defence against a stale file from a
            // previous failed run). COPY_ONLY skips the log chain bookkeeping so this doesn't
            // interfere with the user's own backup strategy.
            InnerDatabaseNameValidator.Require(target.DefaultDatabase);
            // The credentials travel as argv ($1, $2) so the shell never parses them; the database
            // name is validated above because it sits inside the T-SQL text.
            var backup = $"{SqlcmdPath} {ToolsArgs} -S localhost -U \"$2\" -P \"$1\" " +
                         $"-Q \"BACKUP DATABASE [{target.DefaultDatabase}] TO DISK = '{tmp}' WITH FORMAT, INIT, COPY_ONLY\"";
            // 1>&2 routes sqlcmd's progress chatter to stderr so only the binary .bak content
            // ends up in our captured stdout.
            var script = $"{backup} 1>&2 && cat '{tmp}' && rm -f '{tmp}'";

            var bytes = await dockerResolver.Resolve(target.NodeId).ExecCaptureAsync(target.ContainerId, new List<string> { "bash", "-c", script, "krint", target.Password, target.Username }, cancellationToken);
            return new BackupOutput(bytes, "bak");
        }

        public async Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default)
        {
            var tmp = $"/tmp/krint-{Guid.NewGuid():N}.bak";
            InnerDatabaseNameValidator.Require(target.DefaultDatabase);
            var restore = $"{SqlcmdPath} {ToolsArgs} -S localhost -U \"$2\" -P \"$1\" " +
                          $"-Q \"RESTORE DATABASE [{target.DefaultDatabase}] FROM DISK = '{tmp}' WITH REPLACE\"";
            // `cat > file` lands stdin on disk before RESTORE runs - reading a partial file
            // would corrupt the operation.
            var script = $"cat > '{tmp}' && {restore} && rm -f '{tmp}'";

            await dockerResolver.Resolve(target.NodeId).ExecWithStdinAsync(target.ContainerId, new List<string> { "bash", "-c", script, "krint", target.Password, target.Username }, dump, cancellationToken);
        }
    }
}

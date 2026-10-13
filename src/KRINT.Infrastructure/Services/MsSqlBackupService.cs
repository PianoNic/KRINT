using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// MSSQL backup via native BACKUP / RESTORE. The Microsoft image ships sqlcmd at
    /// /opt/mssql-tools18/bin/sqlcmd; we shell into the container so the KRINT host
    /// doesn't need MSSQL tooling installed.
    ///
    /// A backup is a tar of: logins.sql (SQL logins scripted with their password hash and SID,
    /// plus server role memberships) and per user database db-N.name + db-N.bak (BACKUP ...
    /// COPY_ONLY). Keeping the SIDs means database users map back to their logins after a
    /// restore, so an upgrade (which drops the old volume) keeps inner databases and logins. A
    /// non-sysadmin login (adopted external) only gets its default database.
    ///
    /// Older backups are a bare .bak of the default database and still restore.
    ///
    /// Caveats this implementation accepts:
    ///   - sqlcmd must live at the mssql-tools18 path. Containers based on older SQL Server
    ///     images (where it was /opt/mssql-tools/bin/sqlcmd) will fail with a clear error.
    ///   - RESTORE does *not* MOVE the data/log files - it relies on the source's physical
    ///     file paths being free or belonging to the same database on the target, which holds
    ///     for restore-in-place, upgrade and migration into a fresh container.
    ///   - Windows/Entra logins and contained users are not scripted (not used on Linux images).
    /// </summary>
    public sealed class MsSqlBackupService(IDockerServiceResolver dockerResolver) : IBackupService
    {
        public string Engine => "mssql";

        public string FileExtension => "tar";

        // -N enables encryption (required by the v18 driver); -C trusts the self-signed cert the
        // container generates on first boot. -x turns off sqlcmd's $(var) substitution so a
        // database name can't inject into the T-SQL. The password comes from SQLCMDPASSWORD.
        private const string Prelude = """
            set -eo pipefail
            export SQLCMDPASSWORD="$1"
            u="$2"
            sq() { /opt/mssql-tools18/bin/sqlcmd -N -C -S localhost -U "$u" -x "$@" </dev/null; }

            """;

        internal const string DumpScript = Prelude + """
            d=$(mktemp -d /tmp/krint-dump-XXXXXX)
            trap 'rm -rf "$d"' EXIT
            if [ "$(sq -h -1 -W -Q "SET NOCOUNT ON; SELECT IS_SRVROLEMEMBER('sysadmin')" | tr -d '[:space:]')" = "1" ]; then
              sq -b -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 AND source_database_id IS NULL ORDER BY name" > "$d/databases"
              cat > "$d/q.sql" <<'EOF'
            SET NOCOUNT ON;
            SELECT 'IF SUSER_ID(N''' + REPLACE(name, '''', '''''') + ''') IS NULL CREATE LOGIN ' + QUOTENAME(name)
                 + ' WITH PASSWORD = ' + CONVERT(varchar(max), CAST(LOGINPROPERTY(name, 'PasswordHash') AS varbinary(max)), 1)
                 + ' HASHED, SID = ' + CONVERT(varchar(max), sid, 1) + ', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;'
            FROM sys.sql_logins WHERE name NOT LIKE '##%' AND name <> 'sa' AND name <> SUSER_SNAME();
            SELECT 'ALTER SERVER ROLE ' + QUOTENAME(r.name) + ' ADD MEMBER ' + QUOTENAME(m.name) + ';'
            FROM sys.server_role_members rm
            JOIN sys.server_principals r ON r.principal_id = rm.role_principal_id
            JOIN sys.server_principals m ON m.principal_id = rm.member_principal_id
            WHERE m.type = 'S' AND m.name NOT LIKE '##%' AND m.name <> 'sa' AND m.name <> SUSER_SNAME();
            EOF
              sq -b -h -1 -y 0 -i "$d/q.sql" -o "$d/logins.sql"
              rm -f "$d/q.sql"
            else
              printf '%s\n' "$3" > "$d/databases"
            fi
            i=0
            while IFS= read -r name; do
              [ -n "$name" ] || continue
              i=$((i+1))
              printf '%s' "$name" > "$d/db-$i.name"
              b=${name//]/]]}
              # 1>&2 keeps sqlcmd's progress chatter out of the archive on stdout.
              sq -b -Q "BACKUP DATABASE [$b] TO DISK = N'$d/db-$i.bak' WITH FORMAT, INIT, COPY_ONLY" 1>&2
            done < "$d/databases"
            rm -f "$d/databases"
            tar -C "$d" -cf - .
            """;

        // Logins first (errors ignored: an existing login is fine), then each database. A database
        // in use can't be replaced, so it is put in single-user mode (kicking other sessions) first.
        internal const string RestoreTarScript = Prelude + """
            d=$(mktemp -d /tmp/krint-restore-XXXXXX)
            trap 'rm -rf "$d"' EXIT
            tar -C "$d" -xf -
            if [ -s "$d/logins.sql" ]; then sq -i "$d/logins.sql" >/dev/null 2>&1 || true; fi
            for n in "$d"/db-*.name; do
              [ -e "$n" ] || continue
              name=$(cat "$n")
              b=${name//]/]]}
              s=${name//\'/\'\'}
              sq -b -Q "IF DB_ID(N'$s') IS NOT NULL ALTER DATABASE [$b] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$b] FROM DISK = N'${n%.name}.bak' WITH REPLACE; ALTER DATABASE [$b] SET MULTI_USER;" 1>&2
            done
            """;

        // Backups taken before the tar format: a bare .bak of the default database. `cat > file`
        // lands stdin on disk before RESTORE runs - reading a partial file would corrupt it.
        private const string RestoreLegacyScript = Prelude + """
            db="$3"
            tmp=$(mktemp /tmp/krint-XXXXXX)
            trap 'rm -f "$tmp"' EXIT
            cat > "$tmp"
            sq -b -Q "RESTORE DATABASE [${db//]/]]}] FROM DISK = N'$tmp' WITH REPLACE" 1>&2
            """;

        public Task DumpToAsync(BackupTarget target, Stream output, CancellationToken cancellationToken = default)
        {
            var cmd = new List<string> { "bash", "-c", DumpScript, "krint", target.Password, target.Username, target.DefaultDatabase };
            return dockerResolver.Resolve(target.NodeId).ExecToStreamAsync(target.ContainerId, cmd, output, target.Timeout, cancellationToken);
        }

        public async Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default)
        {
            var (isTar, stream) = await BackupFormat.SniffAsync(dump, cancellationToken);
            await using var owned = stream == dump ? null : stream;

            if (!isTar) InnerDatabaseNameValidator.Require(target.DefaultDatabase);
            var script = isTar ? RestoreTarScript : RestoreLegacyScript;
            var cmd = new List<string> { "bash", "-c", script, "krint", target.Password, target.Username, target.DefaultDatabase };
            await dockerResolver.Resolve(target.NodeId).ExecWithStdinAsync(target.ContainerId, cmd, stream, target.Timeout, cancellationToken);
        }
    }
}

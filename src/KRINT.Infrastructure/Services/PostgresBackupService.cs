using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// Backups are a tar of: globals.sql (roles, from pg_dumpall --globals-only), and per database
    /// db-N.name + db-N.dump (pg_dump custom archive). As a superuser every connectable,
    /// non-template database is included, so an upgrade (which drops the old volume) keeps inner
    /// databases and users. A non-superuser (adopted external) can't read other databases, so it
    /// gets only its default database and best-effort roles without passwords.
    ///
    /// Older backups are a bare pg_dump custom archive of the default database and still restore.
    /// Credentials and names travel as argv ($1..$3), so the shell never parses them: an adopted
    /// external's password is whatever the user typed.
    /// </summary>
    public class PostgresBackupService(IDockerServiceResolver dockerResolver) : IBackupService
    {
        protected IDockerService Docker(BackupTarget target) => dockerResolver.Resolve(target.NodeId);

        public virtual string Engine => "postgres";

        public string FileExtension => "tar";

        private const string Prelude = """
            set -eo pipefail
            export PGPASSWORD="$1" PGUSER="$2" PGDATABASE="$3" PGHOST=127.0.0.1
            issuper() { [ "$(psql -X -At -c 'SELECT rolsuper FROM pg_roles WHERE rolname = current_user')" = "t" ]; }

            """;

        internal const string DumpScript = Prelude + """
            d=$(mktemp -d /tmp/krint-dump-XXXXXX)
            trap 'rm -rf "$d"' EXIT
            if issuper; then
              pg_dumpall --globals-only > "$d/globals.sql"
              psql -X -At -c "SELECT datname FROM pg_database WHERE datallowconn AND NOT datistemplate ORDER BY datname" > "$d/databases"
            else
              pg_dumpall --globals-only --no-role-passwords > "$d/globals.sql" 2>/dev/null || : > "$d/globals.sql"
              printf '%s\n' "$PGDATABASE" > "$d/databases"
            fi
            i=0
            while IFS= read -r name; do
              [ -n "$name" ] || continue
              i=$((i+1))
              printf '%s' "$name" > "$d/db-$i.name"
              PGDATABASE="$name" pg_dump -F c -f "$d/db-$i.dump"
            done < "$d/databases"
            rm -f "$d/databases"
            tar -C "$d" -cf - .
            """;

        // Roles first so ownership and grants resolve. Replaying globals.sql also replays the
        // connecting role's password from backup time, which may be stale since a root password
        // reset - so it is set back to the current one right after. Existing databases are
        // cleaned and refilled like the old single-database restore; missing ones are created
        // from the archive (-C). Ownership is kept only for a superuser, who restored the roles.
        internal const string RestoreTarScript = Prelude + """
            d=$(mktemp -d /tmp/krint-restore-XXXXXX)
            trap 'rm -rf "$d"' EXIT
            tar -C "$d" -xf -
            own=--no-owner
            if issuper; then own=; fi
            if [ -s "$d/globals.sql" ]; then
              psql -X -q -f "$d/globals.sql" >/dev/null 2>&1 || true
              printf '%s' "ALTER ROLE CURRENT_USER PASSWORD :'p'" | psql -X -q -v ON_ERROR_STOP=1 -v p="$1" >/dev/null
            fi
            for n in "$d"/db-*.name; do
              [ -e "$n" ] || continue
              name=$(cat "$n")
              f="${n%.name}.dump"
              exists=$(printf '%s' "SELECT 1 FROM pg_database WHERE datname = :'n'" | psql -X -At -v ON_ERROR_STOP=1 -v n="$name")
              if [ "$exists" = "1" ]; then
                pg_restore -d "$name" --clean --if-exists $own "$f"
              else
                pg_restore -d "$PGDATABASE" -C $own "$f"
              fi
            done
            """;

        // Backups taken before the tar format: one custom-format archive of the default database.
        // --clean --if-exists drops existing objects first so re-applying a dump is idempotent.
        // --no-owner skips role re-assignment that would fail if the original owner doesn't exist.
        private const string RestoreLegacyScript =
            "PGPASSWORD=\"$1\" pg_restore -h 127.0.0.1 -U \"$2\" -d \"$3\" --clean --if-exists --no-owner";

        public Task DumpToAsync(BackupTarget target, Stream output, CancellationToken cancellationToken = default)
        {
            var cmd = new List<string> { "bash", "-c", DumpScript, "krint", target.Password, target.Username, target.DefaultDatabase };
            return Docker(target).ExecToStreamAsync(target.ContainerId, cmd, output, target.Timeout, cancellationToken);
        }

        public async Task RestoreAsync(BackupTarget target, Stream dump, CancellationToken cancellationToken = default)
        {
            var (isTar, stream) = await BackupFormat.SniffAsync(dump, cancellationToken);
            await using var owned = stream == dump ? null : stream;

            var script = isTar ? RestoreTarScript : RestoreLegacyScript;
            var cmd = new List<string> { "bash", "-c", script, "krint", target.Password, target.Username, target.DefaultDatabase };
            await Docker(target).ExecWithStdinAsync(target.ContainerId, cmd, stream, target.Timeout, cancellationToken);
        }
    }
}

using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // CockroachDB speaks the Postgres wire protocol - Npgsql talks to it natively, and the
    // information_schema / pg_catalog views are 1:1 enough that our list/select queries work.
    // Differences we have to override:
    //   - No DROP DATABASE … WITH (FORCE) clause syntax.
    //   - No `ctid` MVCC handle, so the row edit/delete code that uses it doesn't work; caps
    //     advertise SupportsRowEdit/Delete=false, so the schema service's defaults are simply
    //     never invoked from the UI.
    //   - No pg_dump compatibility; SupportsBackup=false until we wire `cockroach sql` /
    //     `EXPORT` flows. No backup-service registration here.
    public sealed class CockroachDbInnerDatabaseService : PostgresInnerDatabaseService
    {
        public override string Engine => "cockroachdb";

        protected override string BuildDropSql(string name) => $"DROP DATABASE IF EXISTS \"{name}\"";
    }

    public sealed class CockroachDbInnerUserService : PostgresInnerUserService
    {
        public override string Engine => "cockroachdb";

        // We run CockroachDB with `start-single-node --insecure`, which rejects any password
        // operation ("setting or updating a password is not supported in insecure mode"). Create
        // passwordless login roles instead - in insecure mode they authenticate without one.
        public override async Task CreateAsync(InnerDatabaseTarget target, string name, string password, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(name);
            await using var conn = await OpenAsync(target, cancellationToken);
            await using var cmd = new Npgsql.NpgsqlCommand($"CREATE ROLE \"{name}\" LOGIN", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public override Task ResetPasswordAsync(InnerDatabaseTarget target, string name, string newPassword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("CockroachDB runs in insecure mode, which has no user passwords.");
    }

    public sealed class CockroachDbInnerSchemaService : PostgresInnerSchemaService
    {
        public override string Engine => "cockroachdb";
    }
}

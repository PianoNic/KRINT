using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // TimescaleDB is a Postgres extension shipped as the `timescale/timescaledb` Docker image.
    // The wire protocol, dump format, and admin SQL are all stock Postgres - we just relabel
    // Engine so the resolver maps "timescaledb" to the same code path.
    public sealed class TimescaleDbInnerDatabaseService : PostgresInnerDatabaseService
    {
        public override string Engine => "timescaledb";
    }

    public sealed class TimescaleDbInnerUserService : PostgresInnerUserService
    {
        public override string Engine => "timescaledb";
    }

    public sealed class TimescaleDbInnerSchemaService : PostgresInnerSchemaService
    {
        public override string Engine => "timescaledb";
    }

    public sealed class TimescaleDbBackupService(IDockerService docker) : PostgresBackupService(docker)
    {
        public override string Engine => "timescaledb";
    }
}

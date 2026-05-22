using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // pgvector is Postgres + the `vector` extension preloaded in the image. The wire protocol,
    // dump format, and admin SQL are all stock Postgres - we just relabel Engine and run
    // CREATE EXTENSION on first provision (handled in CreateDatabaseCommand via EngineSpec.InitSql).
    public sealed class PgVectorInnerDatabaseService : PostgresInnerDatabaseService
    {
        public override string Engine => "pgvector";
    }

    public sealed class PgVectorInnerUserService : PostgresInnerUserService
    {
        public override string Engine => "pgvector";
    }

    public sealed class PgVectorInnerSchemaService : PostgresInnerSchemaService
    {
        public override string Engine => "pgvector";
    }

    public sealed class PgVectorBackupService(IDockerService docker) : PostgresBackupService(docker)
    {
        public override string Engine => "pgvector";
    }
}

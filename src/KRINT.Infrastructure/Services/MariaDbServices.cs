using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // MariaDB is wire-compatible with MySQL and ships the same `mysql` / `mysqldump` CLI tools,
    // so we reuse the MySql implementations and only relabel Engine for the resolver lookup.
    public sealed class MariaDbInnerDatabaseService : MySqlInnerDatabaseService
    {
        public override string Engine => "mariadb";
    }

    public sealed class MariaDbInnerUserService : MySqlInnerUserService
    {
        public override string Engine => "mariadb";
    }

    public sealed class MariaDbInnerSchemaService : MySqlInnerSchemaService
    {
        public override string Engine => "mariadb";
    }

    public sealed class MariaDbBackupService(IDockerServiceResolver dockerResolver) : MySqlBackupService(dockerResolver)
    {
        public override string Engine => "mariadb";
    }
}

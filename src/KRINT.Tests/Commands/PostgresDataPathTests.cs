using KRINT.Application.Command.Database;

namespace KRINT.Tests.Commands
{
    public class PostgresDataPathTests
    {
        [Test]
        [Arguments("18.6", "/var/lib/postgresql")]
        [Arguments("18", "/var/lib/postgresql")]
        [Arguments("latest", "/var/lib/postgresql")]
        [Arguments("17.11", "/var/lib/postgresql/data")]
        [Arguments("13", "/var/lib/postgresql/data")]
        [Arguments("latest-pg18", "/var/lib/postgresql")]
        [Arguments("latest-pg17", "/var/lib/postgresql/data")]
        [Arguments("2.17.2-pg16", "/var/lib/postgresql/data")]
        [Arguments("pg18", "/var/lib/postgresql")]
        [Arguments("pg17", "/var/lib/postgresql/data")]
        public async Task Picks_the_layout_the_image_expects(string tag, string expected)
        {
            await Assert.That(CreateDatabaseCommandHandler.PostgresDataPath(tag)).IsEqualTo(expected);
        }

        [Test]
        public async Task Timescale_and_pgvector_specs_follow_the_postgres_rule()
        {
            await Assert.That(CreateDatabaseCommandHandler.ResolveEngineSpec("timescaledb", "latest-pg18").DataPath).IsEqualTo("/var/lib/postgresql");
            await Assert.That(CreateDatabaseCommandHandler.ResolveEngineSpec("timescaledb", "latest-pg16").DataPath).IsEqualTo("/var/lib/postgresql/data");
            await Assert.That(CreateDatabaseCommandHandler.ResolveEngineSpec("pgvector", "pg18").DataPath).IsEqualTo("/var/lib/postgresql");
        }
    }
}

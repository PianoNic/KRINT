using KRINT.Application.Command.Database;

namespace KRINT.Tests.Commands
{
    public class BuildEnvTests
    {
        [Test]
        public async Task Mariadb_always_requests_its_database_even_for_the_default_name()
        {
            var env = CreateDatabaseCommandHandler.BuildEnv("mariadb", "pw", "mariadb", "mariadb");
            await Assert.That(env).Contains("MARIADB_DATABASE=mariadb");
            await Assert.That(env).Contains("MARIADB_ROOT_PASSWORD=pw");
        }

        [Test]
        public async Task Mysql_leaves_the_system_default_alone()
        {
            var env = CreateDatabaseCommandHandler.BuildEnv("mysql", "pw", "mysql", "mysql");
            await Assert.That(env).DoesNotContain("MYSQL_DATABASE=mysql");
        }
    }
}

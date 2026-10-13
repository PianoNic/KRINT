using KRINT.Infrastructure.Services;

namespace KRINT.Tests.Services
{
    public class InnerDatabaseNameValidatorTests
    {
        [Test]
        [Arguments("postgres", "my-db")]
        [Arguments("cassandra", "app_keyspace")]
        [Arguments("couchdb", "app-db_1")]
        [Arguments("mysql", "Analytics")]
        public async Task Accepts_names_the_engine_allows(string engine, string name)
        {
            await Assert.That(() => InnerDatabaseNameValidator.RequireDatabaseForEngine(engine, name)).ThrowsNothing();
        }

        [Test]
        [Arguments("cassandra", "my-keyspace")]
        [Arguments("cassandra", "_keyspace")]
        [Arguments("cassandra", "k0123456789012345678901234567890123456789012345678")]
        [Arguments("couchdb", "AppDb")]
        [Arguments("couchdb", "_users")]
        [Arguments("postgres", "1db")]
        [Arguments("postgres", "")]
        public async Task Rejects_names_the_engine_refuses(string engine, string name)
        {
            await Assert.That(() => InnerDatabaseNameValidator.RequireDatabaseForEngine(engine, name)).Throws<ArgumentException>();
        }

        [Test]
        public async Task MySql_user_longer_than_32_chars_is_rejected()
        {
            var name = new string('u', 33);
            await Assert.That(() => InnerDatabaseNameValidator.RequireUserForEngine("mysql", name)).Throws<ArgumentException>();
            await Assert.That(() => InnerDatabaseNameValidator.RequireUserForEngine("mariadb", name)).ThrowsNothing();
            await Assert.That(() => InnerDatabaseNameValidator.RequireUserForEngine("mysql", new string('u', 32))).ThrowsNothing();
        }
    }
}

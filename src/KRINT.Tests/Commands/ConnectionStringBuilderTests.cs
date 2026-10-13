using KRINT.Application;

namespace KRINT.Tests.Commands
{
    public class ConnectionStringBuilderTests
    {
        [Test]
        public async Task Object_store_strings_are_plain_endpoints_or_complete()
        {
            await Assert.That(ConnectionStringBuilder.Build("qdrant", "localhost", 34000, "default", "key", "_cluster")).IsEqualTo("http://localhost:34000");
            await Assert.That(ConnectionStringBuilder.Build("seaweedfs", "localhost", 34400, "krint", "secret", "default")).IsEqualTo("http://localhost:34400");
            var azurite = ConnectionStringBuilder.Build("azurite", "localhost", 34800, "devstoreaccount1", "unused", "default");
            await Assert.That(azurite).Contains($"AccountKey={ConnectionStringBuilder.AzuriteDevelopmentKey};");
            await Assert.That(azurite).DoesNotContain("<");
        }

        [Test]
        public async Task Sql_strings_keep_credentials_inline()
        {
            await Assert.That(ConnectionStringBuilder.Build("postgres", "localhost", 30000, "postgres", "pw", "app")).IsEqualTo("postgres://postgres:pw@localhost:30000/app");
            await Assert.That(ConnectionStringBuilder.Build("redis", "localhost", 31000, "default", "pw", "0")).IsEqualTo("redis://default:pw@localhost:31000/0");
        }

        [Test]
        [Arguments("postgres", "postgres://us%40er:p%40ss%3Aw%2Fo%3Frd%23%25@localhost:5432/app")]
        [Arguments("mysql", "mysql://us%40er:p%40ss%3Aw%2Fo%3Frd%23%25@localhost:5432/app")]
        [Arguments("mongo", "mongodb://us%40er:p%40ss%3Aw%2Fo%3Frd%23%25@localhost:5432/app?authSource=admin")]
        [Arguments("redis", "redis://default:p%40ss%3Aw%2Fo%3Frd%23%25@localhost:5432/app")]
        [Arguments("cockroachdb", "postgres://us%40er@localhost:5432/app?sslmode=disable")]
        [Arguments("clickhouse", "http://us%40er:p%40ss%3Aw%2Fo%3Frd%23%25@localhost:5432/?database=app")]
        [Arguments("neo4j", "bolt://us%40er:p%40ss%3Aw%2Fo%3Frd%23%25@localhost:5432")]
        public async Task Url_strings_escape_user_and_password(string engine, string expected)
        {
            var url = ConnectionStringBuilder.Build(engine, "localhost", 5432, "us@er", "p@ss:w/o?rd#%", "app");
            await Assert.That(url).IsEqualTo(expected);
        }

        [Test]
        public async Task Escaped_url_round_trips_to_the_original_credentials()
        {
            var url = new Uri(ConnectionStringBuilder.Build("postgres", "db.example", 5432, "us@er", "p@ss:w/o?rd#%", "app"));
            await Assert.That(url.Host).IsEqualTo("db.example");
            await Assert.That(url.AbsolutePath).IsEqualTo("/app");
            var parts = url.UserInfo.Split(':');
            await Assert.That(Uri.UnescapeDataString(parts[0])).IsEqualTo("us@er");
            await Assert.That(Uri.UnescapeDataString(parts[1])).IsEqualTo("p@ss:w/o?rd#%");
        }

        [Test]
        public async Task ClickHouse_query_connection_string_keeps_special_password_intact()
        {
            var target = new KRINT.Infrastructure.Interfaces.InnerDatabaseTarget("clickhouse", "localhost", 8123, "default", "pw;Host=evil;x=y", "default");
            var connectionString = KRINT.Infrastructure.Services.ClickHouseInnerQueryService.BuildConnectionString(target, "app");
            var parsed = new ClickHouse.Client.ADO.ClickHouseConnectionStringBuilder(connectionString);

            await Assert.That(parsed.Password).IsEqualTo("pw;Host=evil;x=y");
            await Assert.That(parsed.Host).IsEqualTo("localhost");
            await Assert.That(parsed.Database).IsEqualTo("app");
            await Assert.That(parsed.Compression).IsFalse();
        }
    }
}

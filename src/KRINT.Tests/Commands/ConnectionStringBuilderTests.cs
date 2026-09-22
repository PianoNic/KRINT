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
    }
}

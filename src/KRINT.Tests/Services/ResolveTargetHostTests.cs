using KRINT.Application;

namespace KRINT.Tests.Services
{
    public class ResolveTargetHostTests
    {
        [Test]
        [Arguments("localhost")]
        [Arguments("Localhost")]
        [Arguments(" LOCALHOST ")]
        [Arguments("127.0.0.1")]
        [Arguments("::1")]
        [Arguments("[::1]")]
        public async Task Loopback_forms_are_rewritten(string host)
        {
            await Assert.That(InnerDatabaseTargetLoader.ResolveTargetHost(host, isManaged: false, isPublic: false)).IsEqualTo("host.docker.internal");
            await Assert.That(InnerDatabaseTargetLoader.ResolveTargetHost(host, isManaged: true, isPublic: false)).IsEqualTo("127.0.0.1");
        }

        [Test]
        public async Task Real_hostnames_are_kept_trimmed()
        {
            await Assert.That(InnerDatabaseTargetLoader.ResolveTargetHost(" db.example.com ", isManaged: false, isPublic: false)).IsEqualTo("db.example.com");
        }
    }
}

using KRINT.Infrastructure.Services;

namespace KRINT.Tests.Services
{
    public class ProbeHostGuardTests
    {
        [Test]
        [Arguments("localhost")]
        [Arguments("127.0.0.1")]
        [Arguments("10.0.0.5")]
        [Arguments("192.168.1.20")]
        [Arguments("db.example.com")]
        [Arguments("host.docker.internal")]
        public async Task Allows_ordinary_and_private_hosts(string host)
        {
            await Assert.That(() => ProbeHostGuard.Require(host)).ThrowsNothing();
        }

        [Test]
        [Arguments("169.254.169.254")]
        [Arguments("169.254.0.1")]
        [Arguments("[fe80::1]")]
        [Arguments("metadata.google.internal")]
        [Arguments("")]
        public async Task Refuses_link_local_and_metadata_hosts(string host)
        {
            await Assert.That(() => ProbeHostGuard.Require(host)).Throws<ArgumentException>();
        }
    }
}

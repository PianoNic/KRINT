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
        [Arguments("host.docker.internal")]
        [Arguments("no-such-host.invalid")]
        public async Task Allows_ordinary_private_and_unresolvable_hosts(string host)
        {
            await Assert.That(async () => await ProbeHostGuard.RequireAsync(host)).ThrowsNothing();
        }

        [Test]
        [Arguments("169.254.169.254")]
        [Arguments("169.254.0.1")]
        [Arguments("2852039166")]            // 169.254.169.254 as a decimal integer
        [Arguments("0xA9FEA9FE")]            // and as hex
        [Arguments("::ffff:169.254.169.254")]
        [Arguments("[fe80::1]")]
        [Arguments("fd00:ec2::254")]
        [Arguments("100.100.100.200")]
        [Arguments("0.0.0.0")]
        [Arguments("metadata.google.internal")]
        [Arguments("")]
        public async Task Refuses_link_local_and_metadata_hosts(string host)
        {
            await Assert.That(async () => await ProbeHostGuard.RequireAsync(host)).Throws<ArgumentException>();
        }
    }
}

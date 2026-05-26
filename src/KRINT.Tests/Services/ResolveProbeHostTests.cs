using KRINT.Application.Command.Database;

namespace KRINT.Tests.Services
{
    /// <summary>
    /// The probe host depends on the container's port binding: localhost-bound containers must
    /// be reached on 127.0.0.1, 0.0.0.0-bound ones can go via host.docker.internal. Regressing
    /// this caused a 60s readiness timeout on every new managed provision.
    /// </summary>
    public class ResolveProbeHostTests
    {
        [Test]
        public async Task ResolveProbeHost_Public_ReturnsHostDockerInternal()
        {
            var host = CreateDatabaseCommandHandler.ResolveProbeHost(isPublic: true);
            await Assert.That(host).IsEqualTo("host.docker.internal");
        }

        [Test]
        public async Task ResolveProbeHost_Localhost_Returns127Loopback()
        {
            var host = CreateDatabaseCommandHandler.ResolveProbeHost(isPublic: false);
            await Assert.That(host).IsEqualTo("127.0.0.1");
        }
    }
}

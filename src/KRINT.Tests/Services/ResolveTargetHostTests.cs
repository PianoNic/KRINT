using KRINT.Application;

namespace KRINT.Tests.Services
{
    /// <summary>
    /// External instances registered with "localhost" must be reached via host.docker.internal
    /// from inside the krint container, exactly like the registration probe. Regressing this
    /// made every post-registration inner-database operation 500 (issue #134).
    /// </summary>
    public class ResolveTargetHostTests
    {
        [Test]
        public async Task ExternalLocalhost_ReturnsHostDockerInternal()
        {
            var host = InnerDatabaseTargetLoader.ResolveTargetHost("localhost", isManaged: false, isPublic: true);
            await Assert.That(host).IsEqualTo("host.docker.internal");
        }

        [Test]
        public async Task External127001_ReturnsHostDockerInternal()
        {
            var host = InnerDatabaseTargetLoader.ResolveTargetHost("127.0.0.1", isManaged: false, isPublic: true);
            await Assert.That(host).IsEqualTo("host.docker.internal");
        }

        [Test]
        public async Task ManagedLocalhostPrivate_Returns127Loopback()
        {
            var host = InnerDatabaseTargetLoader.ResolveTargetHost("localhost", isManaged: true, isPublic: false);
            await Assert.That(host).IsEqualTo("127.0.0.1");
        }

        [Test]
        public async Task ManagedLocalhostPublic_ReturnsHostDockerInternal()
        {
            var host = InnerDatabaseTargetLoader.ResolveTargetHost("localhost", isManaged: true, isPublic: true);
            await Assert.That(host).IsEqualTo("host.docker.internal");
        }

        [Test]
        public async Task RemoteHost_IsUntouched()
        {
            var host = InnerDatabaseTargetLoader.ResolveTargetHost("db.example.com", isManaged: false, isPublic: true);
            await Assert.That(host).IsEqualTo("db.example.com");
        }
    }
}

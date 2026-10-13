using Docker.DotNet.Models;
using KRINT.Application;
using KRINT.Application.Command.Database;
using KRINT.Application.Mappings.Database;
using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;

namespace KRINT.Tests.Services
{
    /// <summary>
    /// Apps on the same machine reach a KRINT instance through one connection string and no
    /// network config: host.docker.internal on Docker Desktop, the bridge gateway on Linux. These
    /// pin the binding and the host KRINT hands out, so nothing ends up reachable from the LAN.
    /// </summary>
    public class AppAccessTests
    {
        private static readonly HostAccess Desktop = new(true, "host.docker.internal", null);
        private static readonly HostAccess Linux = new(false, "172.17.0.1", "172.17.0.1");

        private static KRINT.Domain.DatabaseInstance Instance(bool isManaged = true) => new()
        {
            Engine = "postgres",
            Version = "18",
            DisplayName = "shop",
            ContainerName = "krint-pg-1234abcd",
            Host = "localhost",
            Port = 30000,
            Username = "postgres",
            DatabaseName = "shop",
            IsManaged = isManaged,
        };

        private static InstanceContainerSpec Spec(bool loopbackOnly, string? extraBindIp) => new(
            "postgres", "18", Guid.NewGuid(), "shop", "krint-pg-1234abcd", "pw", "shop", [], 30000, loopbackOnly, "krint-pg-1234abcd:/var/lib/postgresql", extraBindIp);

        private static Dictionary<string, IList<PortBinding>> Bound(params string[] hostIps) => new()
        {
            ["5432/tcp"] = hostIps.Select(ip => new PortBinding { HostIP = ip, HostPort = "30000" }).ToList(),
        };

        [Test]
        public async Task HostBindings_PrivateOnLinux_BindsLoopbackAndBridgeOnly()
        {
            var bindings = InstanceContainerBuilder.HostBindings(Spec(loopbackOnly: true, extraBindIp: "172.17.0.1"));

            await Assert.That(bindings.Select(b => b.HostIP)).IsEquivalentTo(new[] { "127.0.0.1", "172.17.0.1" });
        }

        [Test]
        public async Task HostBindings_PrivateOnDesktop_BindsLoopbackOnly()
        {
            var bindings = InstanceContainerBuilder.HostBindings(Spec(loopbackOnly: true, extraBindIp: null));

            await Assert.That(bindings.Select(b => b.HostIP)).IsEquivalentTo(new[] { "127.0.0.1" });
        }

        [Test]
        public async Task HostBindings_Public_BindsAllInterfaces()
        {
            var bindings = InstanceContainerBuilder.HostBindings(Spec(loopbackOnly: false, extraBindIp: "172.17.0.1"));

            await Assert.That(bindings.Select(b => b.HostIP)).IsEquivalentTo(new[] { "" });
        }

        [Test]
        public async Task AppAccess_Desktop_UsesHostDockerInternal()
        {
            var (host, needsRecreate) = InstanceReachability.AppAccess(Instance(), Desktop, Bound("127.0.0.1"));

            await Assert.That(host).IsEqualTo("host.docker.internal");
            await Assert.That(needsRecreate).IsFalse();
        }

        [Test]
        public async Task AppAccess_LinuxWithBridgeBinding_UsesGateway()
        {
            var (host, needsRecreate) = InstanceReachability.AppAccess(Instance(), Linux, Bound("127.0.0.1", "172.17.0.1"));

            await Assert.That(host).IsEqualTo("172.17.0.1");
            await Assert.That(needsRecreate).IsFalse();
        }

        [Test]
        public async Task AppAccess_LinuxContainerFromBeforeBridgeBinding_NeedsRecreate()
        {
            var (host, needsRecreate) = InstanceReachability.AppAccess(Instance(), Linux, Bound("127.0.0.1"));

            await Assert.That(host).IsNull();
            await Assert.That(needsRecreate).IsTrue();
        }

        [Test]
        public async Task AppAccess_ExternalInstance_HasNoAppHost()
        {
            var (host, needsRecreate) = InstanceReachability.AppAccess(Instance(isManaged: false), Desktop, Bound("127.0.0.1"));

            await Assert.That(host).IsNull();
            await Assert.That(needsRecreate).IsFalse();
        }

        [Test]
        public async Task ToProvisionedDto_WithAppHost_BuildsAppConnectionString()
        {
            var dto = Instance().ToProvisionedDto("s3cret", "postgres://postgres:s3cret@localhost:30000/shop", appHost: "host.docker.internal");

            await Assert.That(dto.AppConnectionString).IsEqualTo("postgres://postgres:s3cret@host.docker.internal:30000/shop");
        }

        [Test]
        public async Task IsDockerDesktop_RecognisesDesktopDaemons()
        {
            await Assert.That(DockerService.IsDockerDesktop("Docker Desktop")).IsTrue();
            await Assert.That(DockerService.IsDockerDesktop("Ubuntu 24.04.1 LTS")).IsFalse();
            await Assert.That(DockerService.IsDockerDesktop(null)).IsFalse();
        }
    }
}

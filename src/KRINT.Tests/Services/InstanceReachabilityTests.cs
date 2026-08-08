using KRINT.Application;
using KRINT.Application.Mappings.Database;

namespace KRINT.Tests.Services
{
    /// <summary>
    /// Every provisioned instance records Host="localhost", which is only an address from a shell on
    /// the machine running the container. These cover the reachability facts the API surfaces so the
    /// UI can stop presenting that sentinel as a connectable endpoint.
    /// </summary>
    public class InstanceReachabilityTests
    {
        private static KRINT.Domain.DatabaseInstance Instance(bool isPublic = true, Guid? nodeId = null, string engine = "postgres", string? containerName = "krint-pg-c77bea94") =>
            new()
            {
                Engine = engine,
                Version = "18.4",
                DisplayName = "ArgonFetch",
                ContainerName = containerName,
                Host = "localhost",
                Port = 30000,
                Username = "postgres",
                DatabaseName = "postgres",
                IsPublic = isPublic,
                NodeId = nodeId,
            };

        [Test]
        public async Task InternalPortFor_KnownEngines_ReturnsContainerPort()
        {
            await Assert.That(InstanceReachability.InternalPortFor("postgres")).IsEqualTo(5432);
            await Assert.That(InstanceReachability.InternalPortFor("mongo")).IsEqualTo(27017);
            await Assert.That(InstanceReachability.InternalPortFor("mssql")).IsEqualTo(1433);
        }

        [Test]
        public async Task InternalPortFor_UnknownEngine_ReturnsNull()
        {
            // Externally-registered instances can name an engine KRINT never provisions.
            await Assert.That(InstanceReachability.InternalPortFor("not-an-engine")).IsNull();
        }

        [Test]
        public async Task IsLoopbackOnly_NodeHosted_IsTrueEvenWhenPublic()
        {
            // The create path forces HostIP=127.0.0.1 whenever NodeId is set, so IsPublic can't
            // widen the binding. This is the case that made the displayed host unusable.
            await Assert.That(InstanceReachability.IsLoopbackOnly(Instance(isPublic: true, nodeId: Guid.NewGuid()))).IsTrue();
        }

        [Test]
        public async Task IsLoopbackOnly_LocalFollowsVisibility()
        {
            await Assert.That(InstanceReachability.IsLoopbackOnly(Instance(isPublic: true))).IsFalse();
            await Assert.That(InstanceReachability.IsLoopbackOnly(Instance(isPublic: false))).IsTrue();
        }

        [Test]
        public async Task IsHostEndpointUsable_FalseForNodeHosted()
        {
            await Assert.That(InstanceReachability.IsHostEndpointUsable(Instance(nodeId: Guid.NewGuid()))).IsFalse();
            await Assert.That(InstanceReachability.IsHostEndpointUsable(Instance())).IsTrue();
        }

        [Test]
        public async Task PickSharedNetwork_SkipsImplicitNetworks()
        {
            await Assert.That(InstanceReachability.PickSharedNetwork(["bridge", "krint_default"])).IsEqualTo("krint_default");
            await Assert.That(InstanceReachability.PickSharedNetwork(["bridge"])).IsNull();
            await Assert.That(InstanceReachability.PickSharedNetwork(null)).IsNull();
        }

        [Test]
        public async Task ToProvisionedDto_NodeHosted_CarriesContainerEndpoint()
        {
            var dto = Instance(nodeId: Guid.NewGuid()).ToProvisionedDto(
                "pw",
                "postgres://postgres:pw@localhost:30000/postgres",
                nodeName: "vps-xl-krint",
                networks: ["bridge", "krint_default"]);

            await Assert.That(dto.NodeName).IsEqualTo("vps-xl-krint");
            await Assert.That(dto.ContainerPort).IsEqualTo(5432);
            await Assert.That(dto.DockerNetwork).IsEqualTo("krint_default");
            await Assert.That(dto.LoopbackOnly).IsTrue();
            // The container endpoint uses the internal port, never the published one.
            await Assert.That(dto.ContainerConnectionString)
                .IsEqualTo("postgres://postgres:pw@krint-pg-c77bea94:5432/postgres");
        }

        [Test]
        public async Task ToProvisionedDto_NoContainer_HasNoContainerEndpoint()
        {
            var dto = Instance(containerName: null).ToProvisionedDto("pw", "postgres://postgres:pw@db.example.com:5432/postgres");

            await Assert.That(dto.ContainerConnectionString).IsNull();
            await Assert.That(dto.DockerNetwork).IsNull();
        }
    }
}

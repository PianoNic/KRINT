using Docker.DotNet.Models;
using KRINT.Application.Command.Database;

namespace KRINT.Tests.Commands
{
    public class InstanceContainerBuilderTests
    {
        private static InstanceContainerSpec Spec(string engine, string version, params string[] plugins) => new(
            engine, version, Guid.NewGuid(), "name", "krint-x-1234", "Pw1secret", "db",
            InstanceContainerBuilder.ResolvePlugins(plugins), 15432, LoopbackOnly: true, "krint-x-1234-data:/data");

        [Test]
        [Arguments("18.1", "18-3.6")]
        [Arguments("17.6", "17-3.5")]
        [Arguments("16", "16-3.5")]
        [Arguments("latest", "latest")]
        public async Task Postgis_tag_follows_the_postgres_major(string version, string expected)
        {
            var (name, tag) = InstanceContainerBuilder.ResolveImage("postgres", version, InstanceContainerBuilder.ResolvePlugins(["postgis"]));
            await Assert.That(name).IsEqualTo("postgis/postgis");
            await Assert.That(tag).IsEqualTo(expected);
        }

        [Test]
        [Arguments("7.4.2", "7.4.0-v8")]
        [Arguments("7.2.5", "7.2.0-v20")]
        [Arguments("8.0.1", "latest")]
        public async Task Redis_stack_tag_follows_the_redis_line(string version, string expected)
        {
            var (_, tag) = InstanceContainerBuilder.ResolveImage("redis", version, InstanceContainerBuilder.ResolvePlugins(["redis-stack"]));
            await Assert.That(tag).IsEqualTo(expected);
        }

        [Test]
        public async Task Vector_plugin_uses_the_pgvector_tag()
        {
            var parameters = InstanceContainerBuilder.Build(Spec("postgres", "18.1", "vector"));
            await Assert.That(parameters.Image).IsEqualTo("pgvector/pgvector:pg18");
        }

        [Test]
        public async Task Two_image_swaps_are_rejected()
        {
            await Assert.That(() => InstanceContainerBuilder.ResolvePlugins(["vector", "postgis"])).Throws<ArgumentException>();
        }

        [Test]
        public async Task Postgis_is_not_offered_on_timescaledb()
        {
            var plugins = InstanceContainerBuilder.ResolvePlugins(["postgis"]);
            await Assert.That(() => InstanceContainerBuilder.RequireSupportedByEngine("timescaledb", plugins)).Throws<ArgumentException>();
        }

        [Test]
        public async Task Neo4j_plugin_flags_are_merged()
        {
            var parameters = InstanceContainerBuilder.Build(Spec("neo4j", "5", "apoc", "graph-data-science"));
            await Assert.That(parameters.Env).Contains("NEO4J_PLUGINS=[\"apoc\",\"graph-data-science\"]");
            await Assert.That(parameters.Env.Count(e => e.StartsWith("NEO4J_PLUGINS="))).IsEqualTo(1);
        }

        [Test]
        public async Task Plugins_are_inferred_from_a_legacy_container()
        {
            var inspect = new ContainerInspectResponse
            {
                Config = new Config { Image = "neo4j:5", Env = ["NEO4J_AUTH=neo4j/pw", "NEO4J_PLUGINS=[\"apoc\",\"graph-data-science\"]"] },
            };
            var keys = InstanceContainerBuilder.InferPluginKeys("neo4j", "5", inspect);
            await Assert.That(keys).Contains("apoc");
            await Assert.That(keys).Contains("graph-data-science");

            var pg = new ContainerInspectResponse { Config = new Config { Image = "pgvector/pgvector:pg17", Env = [] } };
            await Assert.That(InstanceContainerBuilder.InferPluginKeys("postgres", "17.2", pg)).Contains("vector");
        }

        [Test]
        public async Task Storage_bind_is_read_from_the_old_container()
        {
            var inspect = new ContainerInspectResponse
            {
                HostConfig = new HostConfig { Binds = ["C:/krint/data/krint-pg-1:/var/lib/postgresql/data"] },
            };
            await Assert.That(InstanceContainerBuilder.ReadStorageBind(inspect, "/var/lib/postgresql/data"))
                .IsEqualTo("C:/krint/data/krint-pg-1:/var/lib/postgresql/data");

            var volume = new ContainerInspectResponse
            {
                Mounts = [new MountPoint { Type = "volume", Name = "krint-pg-1-data", Destination = "/var/lib/postgresql" }],
            };
            await Assert.That(InstanceContainerBuilder.ReadStorageBind(volume, "/var/lib/postgresql"))
                .IsEqualTo("krint-pg-1-data:/var/lib/postgresql");
        }

        [Test]
        public async Task Rolling_tags_are_detected()
        {
            await Assert.That(InstanceContainerBuilder.IsRollingTag("latest")).IsTrue();
            await Assert.That(InstanceContainerBuilder.IsRollingTag("latest-pg18")).IsTrue();
            await Assert.That(InstanceContainerBuilder.IsRollingTag("18.1")).IsFalse();
        }

        [Test]
        public async Task Weak_mssql_password_is_rejected()
        {
            await Assert.That(() => CreateDatabaseCommandHandler.RequireMsSqlPasswordComplexity("alllowercase")).Throws<ArgumentException>();
            await Assert.That(() => CreateDatabaseCommandHandler.RequireMsSqlPasswordComplexity("Str0ngPass")).ThrowsNothing();
        }
    }
}

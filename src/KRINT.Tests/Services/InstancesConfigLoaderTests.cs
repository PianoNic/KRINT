using KRINT.Application.Options;

namespace KRINT.Tests.Services
{
    /// <summary>
    /// Validates the YAML shape we ship to users in /docs/declarative-instances.md. If this
    /// breaks, the docs lie - which is worse than the parse failing loudly.
    /// </summary>
    public class InstancesConfigLoaderTests
    {
        [Test]
        public async Task Load_NoPathConfigured_ReturnsEmpty()
        {
            var loader = new InstancesConfigLoader(Directory.GetCurrentDirectory(), null);
            var config = loader.Load();
            await Assert.That(config.Instances).IsEmpty();
        }

        [Test]
        public async Task Load_MissingFile_ReturnsEmpty()
        {
            var loader = new InstancesConfigLoader(Directory.GetCurrentDirectory(), "does-not-exist.yaml");
            var config = loader.Load();
            await Assert.That(config.Instances).IsEmpty();
        }

        [Test]
        public async Task Load_ValidYaml_ParsesEveryField()
        {
            using var tempDir = new TempDir();
            var path = Path.Combine(tempDir.Path, "instances.yaml");
            await File.WriteAllTextAsync(path, """
instances:
  - engine: postgres
    version: "18.4"
    display_name: prod-db
    default_database_name: app
    password: "Sec-ret.~"
    is_public: false
    databases:
      - analytics
      - reporting
    users:
      - name: alice
        password: "alicepass-1.~"
        grant_databases: [app, analytics]
""");
            var loader = new InstancesConfigLoader(tempDir.Path, "instances.yaml");
            var config = loader.Load();

            await Assert.That(config.Instances).Count().IsEqualTo(1);
            var spec = config.Instances[0];
            await Assert.That(spec.Engine).IsEqualTo("postgres");
            await Assert.That(spec.Version).IsEqualTo("18.4");
            await Assert.That(spec.DisplayName).IsEqualTo("prod-db");
            await Assert.That(spec.DefaultDatabaseName).IsEqualTo("app");
            await Assert.That(spec.Password).IsEqualTo("Sec-ret.~");
            await Assert.That(spec.IsPublic).IsFalse();
            await Assert.That(spec.Databases).Count().IsEqualTo(2);
            await Assert.That(spec.Databases).Contains("analytics");
            await Assert.That(spec.Users).Count().IsEqualTo(1);
            await Assert.That(spec.Users[0].Name).IsEqualTo("alice");
            await Assert.That(spec.Users[0].Password).IsEqualTo("alicepass-1.~");
            await Assert.That(spec.Users[0].GrantDatabases).Count().IsEqualTo(2);
        }

        [Test]
        public async Task Load_RelativePath_ResolvesAgainstConfigDir()
        {
            using var tempDir = new TempDir();
            var subdir = Path.Combine(tempDir.Path, "sub");
            Directory.CreateDirectory(subdir);
            var file = Path.Combine(subdir, "instances.yaml");
            await File.WriteAllTextAsync(file, "instances: []");

            var loader = new InstancesConfigLoader(subdir, "instances.yaml");
            await Assert.That(loader.ResolvedPath).IsEqualTo(Path.GetFullPath(file));

            var config = loader.Load();
            await Assert.That(config.Instances).IsEmpty();
        }

        [Test]
        public async Task Load_AbsolutePath_BypassesConfigDir()
        {
            using var tempDir = new TempDir();
            var file = Path.Combine(tempDir.Path, "abs.yaml");
            await File.WriteAllTextAsync(file, "instances: []");

            var loader = new InstancesConfigLoader("C:\\nonexistent", file);
            await Assert.That(loader.ResolvedPath).IsEqualTo(file);
        }

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "krint-test-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }
    }
}

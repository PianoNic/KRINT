using KRINT.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace KRINT.Tests.Services
{
    public class FileSystemBackupStorageTests
    {
        private static FileSystemBackupStorage Storage(string root) =>
            new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Backup:Directory"] = root }).Build());

        [Test]
        public async Task Keeps_a_normal_backup_under_its_container_folder()
        {
            var root = Path.Combine(Path.GetTempPath(), "krint-backups-test");
            var full = Storage(root).ResolveInsideRoot("krint-pg-1234", "backup.dump");
            await Assert.That(full).IsEqualTo(Path.Combine(Path.GetFullPath(root), "krint-pg-1234", "backup.dump"));
        }

        [Test]
        [Arguments("../escape", "backup.dump")]
        [Arguments("krint-pg-1234", "../../etc/passwd")]
        [Arguments("krint-pg-1234", "nested/file.dump")]
        [Arguments("", "backup.dump")]
        public async Task Refuses_paths_that_leave_the_root(string container, string file)
        {
            var root = Path.Combine(Path.GetTempPath(), "krint-backups-test");
            await Assert.That(() => Storage(root).ResolveInsideRoot(container, file)).Throws<ArgumentException>();
        }

        [Test]
        public async Task Refuses_a_rooted_container_segment()
        {
            var root = Path.Combine(Path.GetTempPath(), "krint-backups-test");
            var rooted = OperatingSystem.IsWindows() ? @"C:\Windows" : "/etc";
            await Assert.That(() => Storage(root).ResolveInsideRoot(rooted, "backup.dump")).Throws<ArgumentException>();
        }
    }
}

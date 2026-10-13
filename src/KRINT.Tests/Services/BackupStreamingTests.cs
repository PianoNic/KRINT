using System.Formats.Tar;
using KRINT.Application.Command.Backup;
using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace KRINT.Tests.Services
{
    public class ExecScriptsTests
    {
        [Test]
        public async Task Redirects_stdin_for_the_whole_script_not_just_its_last_command()
        {
            var wrapped = ExecScripts.RedirectStdin("cat > /tmp/x && restore /tmp/x", "/tmp/in.bin");
            await Assert.That(wrapped).IsEqualTo("{ cat > /tmp/x && restore /tmp/x\n} < '/tmp/in.bin'");
        }

        [Test]
        public async Task Closing_brace_survives_a_trailing_comment()
        {
            var wrapped = ExecScripts.RedirectStdin("restore # done", "/tmp/in.bin");
            await Assert.That(wrapped.EndsWith("\n} < '/tmp/in.bin'")).IsTrue();
        }

        [Test]
        public async Task Pid_file_line_runs_before_the_script()
        {
            var wrapped = ExecScripts.WithPidFile("pg_restore", "/tmp/p.pid");
            await Assert.That(wrapped).IsEqualTo("{ echo $$ > '/tmp/p.pid'; } 2>/dev/null\npg_restore");
        }

        [Test]
        [Arguments("bash", "-c", true)]
        [Arguments("sh", "-c", true)]
        [Arguments("rm", "-f", false)]
        [Arguments("bash", "-x", false)]
        public async Task Recognises_shell_commands(string program, string flag, bool expected)
        {
            await Assert.That(ExecScripts.IsShellCommand(new[] { program, flag, "x" })).IsEqualTo(expected);
        }
    }

    public class BackupFormatTests
    {
        private static byte[] Tar()
        {
            using var ms = new MemoryStream();
            using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "db-1.name") { DataStream = new MemoryStream("app"u8.ToArray()) });
            }
            return ms.ToArray();
        }

        [Test]
        public async Task Detects_the_multi_database_tar_format()
        {
            await Assert.That(BackupFormat.IsTar(Tar())).IsTrue();
        }

        [Test]
        public async Task Treats_a_legacy_pg_dump_archive_as_not_tar()
        {
            var legacy = "PGDMP"u8.ToArray().Concat(new byte[600]).ToArray();
            await Assert.That(BackupFormat.IsTar(legacy)).IsFalse();
        }

        [Test]
        public async Task Treats_a_short_file_as_not_tar()
        {
            await Assert.That(BackupFormat.IsTar("TAPE"u8)).IsFalse();
        }

        [Test]
        public async Task Sniff_rewinds_a_seekable_stream()
        {
            using var stream = new MemoryStream(Tar());
            var (isTar, result) = await BackupFormat.SniffAsync(stream, CancellationToken.None);
            await Assert.That(isTar).IsTrue();
            await Assert.That(ReferenceEquals(result, stream)).IsTrue();
            await Assert.That(stream.Position).IsEqualTo(0L);
        }

        [Test]
        public async Task Sniff_spools_a_non_seekable_stream_and_keeps_all_bytes()
        {
            var bytes = Tar();
            var (isTar, result) = await BackupFormat.SniffAsync(new ForwardOnlyStream(bytes), CancellationToken.None);
            await using (result)
            {
                using var copy = new MemoryStream();
                await result.CopyToAsync(copy);
                await Assert.That(isTar).IsTrue();
                await Assert.That(copy.ToArray().SequenceEqual(bytes)).IsTrue();
            }
        }

        private sealed class ForwardOnlyStream(byte[] data) : MemoryStream(data)
        {
            public override bool CanSeek => false;
        }
    }

    public class BackupStorageStreamingTests
    {
        private static (FileSystemBackupStorage Storage, string Root) Storage()
        {
            var root = Path.Combine(Path.GetTempPath(), "krint-backups-stream-" + Guid.NewGuid().ToString("N"));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Backup:Directory"] = root }).Build();
            return (new FileSystemBackupStorage(config), root);
        }

        [Test]
        public async Task Streams_the_content_into_the_final_file()
        {
            var (storage, root) = Storage();
            try
            {
                var saved = await storage.SaveAsync("krint-pg-1", "a.tar", (s, ct) => s.WriteAsync(new byte[1234], ct).AsTask());
                await Assert.That(saved.SizeBytes).IsEqualTo(1234L);
                await Assert.That(File.Exists(saved.FullPath)).IsTrue();
                await Assert.That(Directory.GetFiles(Path.GetDirectoryName(saved.FullPath)!).Length).IsEqualTo(1);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Test]
        public async Task Removes_the_partial_file_when_the_dump_fails()
        {
            var (storage, root) = Storage();
            try
            {
                await Assert.That(async () => await storage.SaveAsync("krint-pg-1", "a.tar", async (s, ct) =>
                {
                    await s.WriteAsync(new byte[10], ct);
                    throw new InvalidOperationException("dump failed");
                })).Throws<InvalidOperationException>();

                await Assert.That(Directory.GetFiles(Path.Combine(root, "krint-pg-1")).Length).IsEqualTo(0);
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    public class BackupNamingTests
    {
        [Test]
        public async Task Backups_in_the_same_second_get_distinct_names()
        {
            var now = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
            var a = CreateBackupCommandHandler.BackupFileName("krint-pg-1", now, "tar");
            var b = CreateBackupCommandHandler.BackupFileName("krint-pg-1", now, "tar");
            await Assert.That(a).IsNotEqualTo(b);
            await Assert.That(a.StartsWith("krint-pg-1-20260102-030405-678-")).IsTrue();
            await Assert.That(a.EndsWith(".tar")).IsTrue();
        }

        [Test]
        [Arguments("240", 240)]
        [Arguments("", -1)]
        [Arguments("abc", -1)]
        [Arguments("0", -1)]
        public async Task Reads_the_exec_timeout_from_configuration(string value, int expectedMinutes)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Backup:ExecTimeoutMinutes"] = value }).Build();
            var timeout = BackupTarget.ConfiguredTimeout(config);
            await Assert.That(timeout).IsEqualTo(expectedMinutes < 0 ? null : TimeSpan.FromMinutes(expectedMinutes));
        }
    }
}

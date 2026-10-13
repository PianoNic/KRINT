using KRINT.Infrastructure.Services;

namespace KRINT.Tests.Services
{
    public class ExecLineEndingsTests
    {
        [Test]
        public async Task NormalizeLineEndings_StripsCarriageReturnsFromScripts()
        {
            var command = new List<string> { "bash", "-c", "set -eo pipefail\r\necho ok\r\n", "krint" };

            var normalized = DockerService.NormalizeLineEndings(command);

            await Assert.That(normalized[2]).IsEqualTo("set -eo pipefail\necho ok\n");
            await Assert.That(normalized.Any(a => a.Contains('\r'))).IsFalse();
        }

        [Test]
        public async Task NormalizeLineEndings_LeavesPlainArgumentsUntouched()
        {
            var command = new List<string> { "pg_dump", "-U", "postgres" };

            var normalized = DockerService.NormalizeLineEndings(command);

            await Assert.That(normalized).IsEquivalentTo(command);
        }
    }
}

using System.Diagnostics;
using System.Threading.Tasks;

namespace KRINT.Tests.E2E;

/// <summary>
/// Spawns a stand-alone postgres container on the host's docker daemon, tagged with the labels
/// `docker compose` would write. The guided-migration wizard surfaces a "Migrate into KRINT"
/// button for any discovered candidate whose <c>com.docker.compose.project</c> label is set;
/// stamping the labels by hand here is faster than booting a real compose stack just to make
/// the wizard happy.
///
/// Shelling out to the docker CLI keeps the helper readable - we already trust the CLI is
/// available wherever the e2e suite runs (KrintStack uses it via FluentDocker).
/// </summary>
internal sealed class MigrationSourceContainer : IAsyncDisposable
{
    public required string ContainerName { get; init; }
    public required int HostPort { get; init; }
    public required string Password { get; init; }
    public required string Database { get; init; }
    public required string ComposeProject { get; init; }
    public required string ComposeService { get; init; }

    public static async Task<MigrationSourceContainer> StartPostgresAsync()
    {
        // Random suffix keeps reruns from colliding when a previous failed test left state.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"krint-mig-src-{suffix}";
        // High random host port to avoid clashing with the e2e stack's published ports.
        var port = 24000 + Random.Shared.Next(0, 1000);
        var password = "src-test";
        var database = "src_db";
        var project = $"krint-mig-{suffix}";
        var service = "db";
        // We point compose.project.config_files at an absolute-but-non-existent path purely
        // because the wizard renders it back to the user as "remove this from <path>". The
        // discovery code only reads it; nothing on the test side opens it.
        var fakeCompose = $"/tmp/{project}/compose.yml";

        await RunDockerAsync(
            "run", "-d",
            "--name", name,
            "--label", "com.docker.compose.project=" + project,
            "--label", "com.docker.compose.service=" + service,
            "--label", "com.docker.compose.project.config_files=" + fakeCompose,
            "-e", "POSTGRES_PASSWORD=" + password,
            "-e", "POSTGRES_DB=" + database,
            "-p", $"{port}:5432",
            "postgres:18.3");

        await WaitForReadyAsync(name);

        return new MigrationSourceContainer
        {
            ContainerName = name,
            HostPort = port,
            Password = password,
            Database = database,
            ComposeProject = project,
            ComposeService = service,
        };
    }

    public async ValueTask DisposeAsync()
    {
        // -f handles stop+rm in one call; ignore failure because the test may have already
        // removed it (e.g. if KRINT adopted the container during the migration).
        try { await RunDockerAsync("rm", "-f", ContainerName); } catch { }
    }

    private static async Task WaitForReadyAsync(string name)
    {
        // pg_isready inside the container - same readiness check the e2e stack's compose uses.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await RunDockerAsync("exec", name, "pg_isready", "-U", "postgres");
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }
        throw new TimeoutException($"postgres source container {name} did not become ready");
    }

    private static async Task RunDockerAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start docker CLI");
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"docker {string.Join(' ', args)} failed: {err}");
        }
    }
}

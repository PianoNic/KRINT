using System.Threading.Tasks;

namespace KRINT.Tests.Integration;

/// <summary>
/// Spawns a stand-alone postgres container on the host's docker daemon, tagged with the labels
/// `docker compose` would write. Discovery offers a guided migration for any candidate whose
/// <c>com.docker.compose.project</c> label is set; stamping the labels by hand here is faster
/// than booting a real compose stack.
/// </summary>
internal sealed class MigrationSourceContainer : IAsyncDisposable
{
    public required string ContainerName { get; init; }
    public required int HostPort { get; init; }
    public required string Password { get; init; }
    public required string Database { get; init; }
    public required string ComposeProject { get; init; }
    public required string ComposeService { get; init; }
    public required string ComposeFile { get; init; }

    public static async Task<MigrationSourceContainer> StartPostgresAsync()
    {
        // Random suffix keeps reruns from colliding when a previous failed test left state.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var name = $"krint-mig-src-{suffix}";
        // High random host port to avoid clashing with the stack's published ports.
        var port = 24000 + Random.Shared.Next(0, 1000);
        var password = "src-test";
        var database = "src_db";
        var project = $"krint-mig-{suffix}";
        var service = "db";
        // An absolute-but-non-existent path: discovery only reads the label back.
        var fakeCompose = $"/tmp/{project}/compose.yml";

        await DockerCli.RunCheckedAsync(
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
            ComposeFile = fakeCompose,
        };
    }

    public async ValueTask DisposeAsync()
    {
        // -f handles stop+rm in one call; ignore failure in case it is already gone.
        try { await DockerCli.RunAsync("rm", "-f", ContainerName); } catch { }
    }

    private static async Task WaitForReadyAsync(string name)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var result = await DockerCli.RunAsync("exec", name, "pg_isready", "-U", "postgres");
            if (result.ExitCode == 0) return;
            await Task.Delay(500);
        }
        throw new TimeoutException($"postgres source container {name} did not become ready");
    }
}

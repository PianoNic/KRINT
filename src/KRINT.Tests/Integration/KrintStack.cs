using System.Net.Http;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;

namespace KRINT.Tests.Integration;

/// <summary>
/// Boots the throwaway KRINT stack defined in <c>integration/compose.yml</c> (postgres plus the
/// KRINT image built from this repo's Dockerfile) via Ductus.FluentDocker. The stack is started
/// once per test session and torn down at the end. Tests reach the API at <see cref="AppUrl"/>.
///
/// KRINT runs with local password login, so no identity provider is part of the stack. Its
/// host port is fixed in the compose file; edit both places if it conflicts on your machine.
/// </summary>
public sealed class KrintStack : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static KrintStack? _shared;

    public const int KrintHostPort = 18081;
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Integration-Admin-Pass-2026";

    public required ICompositeService Compose { get; init; }

    public string AppUrl => $"http://localhost:{KrintHostPort}";

    /// <summary>
    /// Set KRINT_SKIP_INTEGRATION=1 (or the older KRINT_SKIP_E2E=1) to run only the unit tests
    /// without Docker. Integration tests then skip themselves instead of booting the stack.
    /// </summary>
    public static bool Skipped =>
        IsSet("KRINT_SKIP_INTEGRATION") || IsSet("KRINT_SKIP_E2E");

    private static bool IsSet(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);

    public static async Task<KrintStack> GetAsync()
    {
        if (Skipped)
            TUnit.Core.Skip.Test("Integration tests disabled (KRINT_SKIP_INTEGRATION=1).");
        if (_shared is not null) return _shared;
        await Gate.WaitAsync();
        try
        {
            _shared ??= await BuildAsync();
            return _shared;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task StopSharedAsync()
    {
        if (_shared is null) return;
        await _shared.DisposeAsync();
        _shared = null;
    }

    private static async Task<KrintStack> BuildAsync()
    {
        var repoRoot = FindRepoRoot();
        var composeFile = Path.Combine(repoRoot, "integration", "compose.yml");

        // Build + start the whole stack. depends_on with condition: service_healthy gates the
        // krint container on postgres; KRINT itself is probed from the host once compose returns.
        var compose = new Builder()
            .UseContainer()
            .UseCompose()
            .FromFile(composeFile)
            .RemoveOrphans()
            .ForceRecreate()
            .ForceBuild()
            .Build()
            .Start();

        await WaitForKrintReadyAsync($"http://localhost:{KrintHostPort}/api/App", TimeSpan.FromMinutes(3));

        return new KrintStack { Compose = compose };
    }

    private static async Task WaitForKrintReadyAsync(string url, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var res = await http.GetAsync(url);
                if (res.IsSuccessStatusCode) return;
            }
            catch (Exception ex) { last = ex; }
            await Task.Delay(1000);
        }
        throw new TimeoutException($"KRINT did not become ready at {url} within {timeout}.", last);
    }

    public ValueTask DisposeAsync()
    {
        try { Compose.Stop(); } catch { }
        try { Compose.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KRINT.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Cannot locate repo root (no KRINT.slnx found walking up from " + AppContext.BaseDirectory + ").");
    }
}

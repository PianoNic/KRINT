using System.Net.Http;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;

namespace KRINT.Tests.E2E;

/// <summary>
/// Boots the throwaway KRINT stack defined in <c>e2e/compose.e2e.yml</c> (postgres,
/// mock-oauth2-server, the bundled KRINT image built from this repo's Dockerfile) via
/// Ductus.FluentDocker. The stack is started once per test session and torn down at the
/// end. Tests reach the KRINT app on the host at <see cref="AppUrl"/>.
///
/// Host ports are fixed in the compose file (postgres:15434, mock-oauth2:18080,
/// krint:18081) so the krint container can have its OIDC env baked in at boot. Random
/// ports would force a multi-pass bring-up that isn't worth the complexity here.
/// </summary>
public sealed class KrintStack : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static KrintStack? _shared;

    private const int KrintHostPort = 18081;
    private const int MockOauthHostPort = 18080;
    private const string OidcIssuerId = "default";

    public required ICompositeService Compose { get; init; }

    public string AppUrl => $"http://localhost:{KrintHostPort}";
    public string OidcAuthority => $"http://localhost:{MockOauthHostPort}/{OidcIssuerId}";

    public static async Task<KrintStack> GetAsync()
    {
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
        var composeFile = Path.Combine(repoRoot, "e2e", "compose.e2e.yml");

        // Build + start the whole stack. Compose's depends_on with condition:
        // service_healthy gates the krint container on postgres/mock-oauth2 healthchecks.
        // KRINT itself has no in-container healthcheck (distroless = no shell), so we
        // probe its /api/App from the host once compose returns.
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

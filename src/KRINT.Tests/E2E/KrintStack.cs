using System.Net.Http;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Testcontainers.PostgreSql;

namespace KRINT.Tests.E2E;

/// <summary>
/// Boots a complete throwaway KRINT stack inside Docker — Postgres, Keycloak with the
/// pre-seeded e2e_runner user, and the bundled KRINT image (frontend + API) built from
/// this repo's Dockerfile. The stack is asynchronously initialised once per test session
/// via <see cref="GetAsync"/>, and disposed via <see cref="StopAsync"/> from a teardown
/// hook. Each test reads the URLs from the live <see cref="KrintStack"/> instance.
///
/// All three containers share a Docker network so they can reach each other by name
/// (postgres / keycloak / krint). Tests reach the KRINT app from the host via
/// <see cref="AppUrl"/> on a randomly assigned port.
/// </summary>
public sealed class KrintStack : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static KrintStack? _shared;

    private const string PostgresPassword = "postgres-test";
    private const string PostgresDb = "krint-test";
    private const string KeycloakRealmName = "krint";
    private const string OidcClientId = "krint";

    public required INetwork Network { get; init; }
    public required PostgreSqlContainer Postgres { get; init; }
    public required IContainer Keycloak { get; init; }
    public required IContainer Krint { get; init; }
    public required IFutureDockerImage KrintImage { get; init; }

    public string AppUrl => $"http://{Krint.Hostname}:{Krint.GetMappedPublicPort(8080)}";
    public string KeycloakUrl => $"http://{Keycloak.Hostname}:{Keycloak.GetMappedPublicPort(8080)}";
    public string KeycloakRealmUrl => $"{KeycloakUrl}/realms/{KeycloakRealmName}";
    public string TestUsername => "e2e_runner";
    public string TestPassword => "Test1234!";

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
        // Resolve the repo root so we can hand it to the image builder as the docker
        // context and read the realm import file.
        var repoRoot = FindRepoRoot();
        var realmFile = Path.Combine(repoRoot, "keycloak", "krint-realm.json");
        var realmJson = await File.ReadAllBytesAsync(realmFile);

        // 1) Build the bundled KRINT image. The Dockerfile is at src/KRINT.API/Dockerfile
        //    and expects the repo root as its build context.
        var image = new ImageFromDockerfileBuilder()
            .WithName($"krint-e2e:{Guid.NewGuid():N}")
            .WithDockerfileDirectory(repoRoot)
            .WithDockerfile("src/KRINT.API/Dockerfile")
            .WithCleanUp(true)
            .Build();
        await image.CreateAsync();

        var network = new NetworkBuilder().WithName($"krint-e2e-{Guid.NewGuid():N}").Build();
        await network.CreateAsync();

        var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:18.3")
            .WithNetwork(network)
            .WithNetworkAliases("postgres")
            .WithDatabase(PostgresDb)
            .WithUsername("postgres")
            .WithPassword(PostgresPassword)
            .Build();
        await postgres.StartAsync();

        // Plain ContainerBuilder so we control the command ourselves — the Testcontainers
        // Keycloak module already injects start-dev which collides with our --import-realm flag.
        var keycloak = new ContainerBuilder()
            .WithImage("quay.io/keycloak/keycloak:26.6")
            .WithNetwork(network)
            .WithNetworkAliases("keycloak")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
            .WithEnvironment("KC_HTTP_PORT", "8080")
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithCommand("start-dev", "--import-realm")
            // Pass the realm JSON as a byte array so Testcontainers writes it as a file
            // inside the container. The (string, string) overload sometimes creates the
            // target as a bind-mounted directory which Keycloak then refuses to import.
            .WithResourceMapping(realmJson, "/opt/keycloak/data/import/krint-realm.json")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath($"/realms/{KeycloakRealmName}/.well-known/openid-configuration")))
            .Build();
        await keycloak.StartAsync();

        // The Angular OIDC client runs in the BROWSER (on the host), so the authority
        // must resolve from there — localhost:<host-port>, not the docker-internal hostname.
        // The API also calls this URL for JWKS lookup, but host.docker.internal is reachable
        // from inside containers on Docker Desktop, so localhost works too once we map it.
        var keycloakHostPort = keycloak.GetMappedPublicPort(8080);
        var oidcAuthority = $"http://localhost:{keycloakHostPort}/realms/{KeycloakRealmName}";

        var connectionString = $"Host=postgres;Port=5432;Database={PostgresDb};Username=postgres;Password={PostgresPassword}";

        var krint = new ContainerBuilder()
            .WithImage(image)
            .WithNetwork(network)
            .WithNetworkAliases("krint")
            .WithExtraHost("host.docker.internal", "host-gateway")
            // The KRINT app provisions databases by talking to the host Docker daemon.
            // Mount the host's docker socket into the container so DockerService can reach it.
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production")
            .WithEnvironment("ConnectionStrings__KrintDatabase", connectionString)
            .WithEnvironment("Oidc__Authority", oidcAuthority)
            .WithEnvironment("Oidc__RequireHttpsMetadata", "false")
            .WithEnvironment("Oidc__ClientId", OidcClientId)
            .WithEnvironment("Oidc__RedirectUri", "http://localhost/")
            .WithEnvironment("Oidc__PostLogoutRedirectUri", "http://localhost/")
            .WithEnvironment("Oidc__Scope", "openid profile email roles")
            .WithEnvironment("Cors__AllowedOrigins__0", "http://localhost")
            .WithEnvironment("Vault__MasterKey", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))
            // /api/App is AllowAnonymous and returns 200 with the OIDC config — perfect
            // readiness probe. /openapi/v1.json is gated by IsDevelopment() so it 404s here.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/api/App")))
            .Build();
        await krint.StartAsync();

        return new KrintStack
        {
            Network = network,
            Postgres = postgres,
            Keycloak = keycloak,
            Krint = krint,
            KrintImage = image,
        };
    }

    public async ValueTask DisposeAsync()
    {
        try { await Krint.DisposeAsync(); } catch { }
        try { await Keycloak.DisposeAsync(); } catch { }
        try { await Postgres.DisposeAsync(); } catch { }
        try { await Network.DisposeAsync(); } catch { }
        try { await KrintImage.DisposeAsync(); } catch { }
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

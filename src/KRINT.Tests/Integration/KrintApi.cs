using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace KRINT.Tests.Integration;

/// <summary>
/// Authenticated HTTP client for the integration stack. Signs in once through the local-login
/// session endpoint and re-signs in before the short-lived access token runs out, so long
/// suites (upgrades, SQL Server) never hit a 401 halfway. Responses are read as
/// <see cref="JsonNode"/> so the tests pin the wire contract, not the C# DTO types.
/// </summary>
public sealed class KrintApi
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static KrintApi? _shared;

    private readonly HttpClient _http;
    private string? _token;
    private DateTime _tokenIssuedAt;

    public KrintStack Stack { get; }

    private KrintApi(KrintStack stack)
    {
        Stack = stack;
        // Provision, upgrade and SQL Server restores are synchronous and can take minutes.
        _http = new HttpClient { BaseAddress = new Uri(stack.AppUrl), Timeout = TimeSpan.FromMinutes(15) };
    }

    public static async Task<KrintApi> GetAsync()
    {
        var stack = await KrintStack.GetAsync();
        if (_shared is not null) return _shared;
        await Gate.WaitAsync();
        try
        {
            _shared ??= new KrintApi(stack);
            return _shared;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Signs in with the given credentials and returns the raw response.</summary>
    public Task<HttpResponseMessage> LoginAsync(string identifier, string password) =>
        _http.PostAsJsonAsync("/auth/session/login", new { identifier, password });

    private async Task<string> TokenAsync()
    {
        // Access tokens live fifteen minutes; renew well before that.
        if (_token is not null && DateTime.UtcNow - _tokenIssuedAt < TimeSpan.FromMinutes(10)) return _token;
        using var res = await LoginAsync(KrintStack.AdminUserName, KrintStack.AdminPassword);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Login failed ({(int)res.StatusCode}): {body}");
        _token = JsonNode.Parse(body)!["accessToken"]!.GetValue<string>();
        _tokenIssuedAt = DateTime.UtcNow;
        return _token;
    }

    /// <summary>Sends a request with the admin bearer token. Does not check the status code.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, bool authenticated = true)
    {
        using var req = new HttpRequestMessage(method, path);
        if (authenticated)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return await _http.SendAsync(req);
    }

    /// <summary>Sends a request, asserts a 2xx and returns the JSON body (null for 204).</summary>
    public async Task<JsonNode?> SendOkAsync(HttpMethod method, string path, object? body = null)
    {
        using var res = await SendAsync(method, path, body);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"{method} {path} returned {(int)res.StatusCode} {res.StatusCode}: {text}", null, res.StatusCode);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public async Task<JsonNode> GetAsync(string path) =>
        await SendOkAsync(HttpMethod.Get, path) ?? throw new InvalidOperationException($"GET {path} returned no body");

    public Task<JsonNode?> PostAsync(string path, object? body = null) => SendOkAsync(HttpMethod.Post, path, body);
    public Task<JsonNode?> PatchAsync(string path, object body) => SendOkAsync(HttpMethod.Patch, path, body);
    public Task<JsonNode?> DeleteAsync(string path) => SendOkAsync(HttpMethod.Delete, path);

    /// <summary>A valid admin access token, for SignalR hubs.</summary>
    public Task<string> EnsureTokenAsync() => TokenAsync();

    // ---- Instances ----

    /// <summary>First version the catalog offers for an engine (the newest).</summary>
    public async Task<string> LatestVersionAsync(string engine)
    {
        var supported = (await GetAsync("/api/Database/supported")).AsArray();
        var entry = supported.First(e => e!["key"]!.GetValue<string>() == engine)!;
        return entry["versions"]!.AsArray().First()!.GetValue<string>();
    }

    /// <summary>
    /// Provisions an instance through POST /api/Database/provision. The returned handle deletes
    /// the instance on dispose, so `await using` keeps runs repeatable even when an assert fails.
    /// </summary>
    public async Task<Instance> ProvisionAsync(
        string engine,
        string version,
        string prefix,
        string? defaultDatabase = null,
        IReadOnlyList<string>? databases = null,
        IReadOnlyList<(string Name, string[] Grants)>? users = null)
    {
        var displayName = $"it_{prefix}_{DateTime.UtcNow:HHmmssfff}";
        var result = await PostAsync("/api/Database/provision", new
        {
            engine,
            version,
            displayName,
            defaultDatabaseName = defaultDatabase,
            databases = databases ?? [],
            users = (users ?? []).Select(u => new { name = u.Name, grantDatabases = u.Grants }).ToArray(),
        }) ?? throw new InvalidOperationException("Provision returned no body");

        var instance = result["instance"]!;
        var warnings = result["warnings"]?.AsArray().Select(w => w!.GetValue<string>()).ToArray() ?? [];
        var userPasswords = result["users"]!.AsArray()
            .ToDictionary(u => u!["name"]!.GetValue<string>(), u => u!["password"]!.GetValue<string>());

        return new Instance(this)
        {
            Id = instance["id"]!.GetValue<Guid>(),
            DisplayName = displayName,
            ContainerName = instance["containerName"]!.GetValue<string>(),
            Engine = engine,
            Username = instance["username"]!.GetValue<string>(),
            Password = instance["password"]!.GetValue<string>(),
            ConnectionString = instance["connectionString"]!.GetValue<string>(),
            UserPasswords = userPasswords,
            Warnings = warnings,
            Raw = result,
        };
    }

    public async Task<JsonArray> ListInstancesAsync() => (await GetAsync("/api/Database")).AsArray();

    public async Task<JsonNode?> FindInstanceAsync(Guid id) =>
        (await ListInstancesAsync()).FirstOrDefault(i => i!["id"]!.GetValue<Guid>() == id);

    /// <summary>Runs SQL through the query console endpoint and returns its result.</summary>
    public async Task<JsonNode> QueryAsync(Guid id, string database, string sql) =>
        await PostAsync($"/api/Database/{id}/query", new { database, sql })
        ?? throw new InvalidOperationException("Query returned no body");

    /// <summary>First cell of the first row of a query, as text.</summary>
    public async Task<string?> ScalarAsync(Guid id, string database, string sql)
    {
        var result = await QueryAsync(id, database, sql);
        return result["rows"]!.AsArray().FirstOrDefault()?.AsArray().FirstOrDefault()?.GetValue<string>();
    }

    /// <summary>Retries until <paramref name="probe"/> stops throwing; engines sometimes accept
    /// connections a moment after the container reports ready, and restores restart them.</summary>
    public static async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        while (true)
        {
            try { return await probe(); }
            catch when (DateTime.UtcNow < deadline) { await Task.Delay(1000); }
        }
    }

    // ---- Backups ----

    public async Task<Guid> CreateBackupAsync(Guid instanceId)
    {
        var entry = await PostAsync($"/api/Backups/instance/{instanceId}")
            ?? throw new InvalidOperationException("Backup returned no body");
        return entry["id"]!.GetValue<Guid>();
    }

    public Task RestoreBackupAsync(Guid backupId) => PostAsync($"/api/Backups/{backupId}/restore");

    public sealed class Instance(KrintApi api) : IAsyncDisposable
    {
        public required Guid Id { get; init; }
        public required string DisplayName { get; init; }
        public required string ContainerName { get; set; }
        public required string Engine { get; init; }
        public required string Username { get; init; }
        public required string Password { get; init; }
        public required string ConnectionString { get; init; }
        public required IReadOnlyDictionary<string, string> UserPasswords { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
        public required JsonNode Raw { get; init; }

        /// <summary>Set when a test already deleted the instance itself.</summary>
        public bool Deleted { get; set; }

        public async ValueTask DisposeAsync()
        {
            if (Deleted) return;
            try
            {
                using var res = await api.SendAsync(HttpMethod.Delete, $"/api/Database/{Id}");
                if (res.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.NotFound))
                    Console.WriteLine($"Cleanup of {DisplayName} returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
            }
            catch (Exception ex)
            {
                // Don't mask the real test failure with cleanup noise.
                Console.WriteLine($"Cleanup of {DisplayName} failed: {ex.Message}");
            }
        }
    }
}

using KRINT.API;
using KRINT.API.Extensions;
using KRINT.API.Hubs;
using KRINT.API.Nodes;
using KRINT.API.OpenApi;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Toamaisutaa.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// KRINT runs in one of two roles from the same image. "node" is a stripped worker that does nothing
// but execute Docker work on its own host and dial OUT to the control plane over SignalR; it skips the
// UI, app database, auth and user-facing endpoints entirely. Anything else is the full control plane.
if (string.Equals(builder.Configuration["Krint:Role"], "node", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDocker(builder.Configuration);
    // The node executes Docker AND database operations locally; it needs the engine services. The
    // inner-service resolvers depend on INodeRpc, which is a no-op stub here (a node never re-routes).
    builder.Services.AddSingleton<KRINT.Infrastructure.Interfaces.INodeRpc, OfflineNodeRpc>();
    builder.Services.AddInnerDatabases();
    // A node always uses its own daemon, so the backup/lifecycle services resolve Docker locally
    // (the control-plane DockerServiceResolver needs the hub + registry, which a node doesn't have).
    builder.Services.AddScoped<KRINT.Infrastructure.Interfaces.IDockerServiceResolver, LocalDockerServiceResolver>();
    builder.Services.AddHostedService<NodeAgentHostedService>();

    var nodeApp = builder.Build();
    nodeApp.MapGet("/health", () => Results.Ok(new { status = "ok", role = "node" }));
    nodeApp.Run();
    return;
}

builder.Services.AddKrintConfig(builder.Environment);

// No identity provider configured: run with local password login instead (see LocalLogin.cs).
var localLogin = LocalLogin.IsEnabled(builder.Configuration);
if (localLogin)
    LocalLogin.AddDerivedSigningKey(builder);

builder.Services.AddSpaStaticFiles(options => { options.RootPath = "wwwroot"; });

builder.Services.AddControllers();
builder.Services.AddSignalR(options =>
{
    // What a browser may send in one message. Keystrokes and pasted text for the container
    // shell are the largest legitimate payload; 1 MB leaves room for a big paste without letting
    // any signed-in client push arbitrary amounts through the hubs.
    options.MaximumReceiveMessageSize = 1024 * 1024;
})
.AddHubOptions<NodeHub>(options =>
{
    // Node RPC returns whole backup dumps and container logs as single messages, and a node is
    // authenticated by a pre-shared token rather than a browser session. The cap is lifted for
    // this hub alone (dumps are already fully buffered in memory).
    options.MaximumReceiveMessageSize = null;
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<OAuth2SecuritySchemeTransformer>();
    options.AddOperationTransformer<AnonymousOperationTransformer>();
});

builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });
builder.Services.AddScoped<KRINT.Application.ConfigManagedGuard>();

builder.Services.AddKrintDatabase(builder.Configuration);

// Fail fast on a bad vault key, say what the process runs with, and report an unreachable
// Docker daemon at boot instead of on the first click.
builder.Services.AddHostedService<KrintStartupCheck>();
builder.Services.AddKrintHealthChecks();
// Unhandled exceptions become RFC 9457 problem details (no stack trace) instead of an empty 500.
builder.Services.AddProblemDetails();

builder.Services.AddDocker(builder.Configuration);

builder.Services.AddSecrets();

builder.Services.AddInnerDatabases();

builder.Services.AddCatalog();

// Live registry of nodes connected over /hubs/node (in-memory; node details are persisted in the DB).
builder.Services.AddSingleton<INodeRegistry, NodeRegistry>();
// Routes Docker operations to the local daemon or a node over SignalR, based on the instance's NodeId.
builder.Services.AddScoped<KRINT.Infrastructure.Interfaces.IDockerServiceResolver, DockerServiceResolver>();
// Dispatches inner-DB operations to a node when the target carries a NodeId (used by the routing resolvers).
builder.Services.AddSingleton<KRINT.Infrastructure.Interfaces.INodeRpc, NodeRpc>();
// Bridges node-originated streamed output (container logs) back to the browser hub.
builder.Services.AddSingleton<INodeStreamRelay, NodeStreamRelay>();

builder.Services.AddHostedService<KRINT.API.BackupSchedulerHostedService>();
builder.Services.AddHostedService<KRINT.API.InstanceReconciliationHostedService>();
builder.Services.AddHostedService<KRINT.API.NodeReconciliationHostedService>();

// Defaults to no cross-origin allowlist when unset. The desktop build serves the SPA
// same-origin from the sidecar, so it needs none; server deployments set it explicitly.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Toamaisutaa owns the resource-server half of OIDC: discovery against Oidc:InternalAuthority, issuer
// validation against Oidc:Authority, raw JWT claim names (no WS-Federation remapping), userinfo
// enrichment for issuers that keep roles out of the access token, and the ?access_token= read the
// browser hubs need (scoped by Oidc:QueryToken in appsettings.json so /hubs/node keeps its own token).
builder.Services.AddToamaisutaaBearer(builder.Configuration);
// Authenticated by default; opt out per endpoint with [AllowAnonymous].
builder.Services.AddToamaisutaaAuthorization(builder.Configuration);
// ICurrentUser for activity-log actor names. No provisioning: the IdP owns the users, KRINT keeps none.
builder.Services.AddToamaisutaaCurrentUser();
if (localLogin)
    builder.Services.AddKrintLocalLogin(builder.Configuration);
// Deployments already set Krint:PublicUrl; let it feed the login redirect derivation too, so nobody
// has to configure the same URL twice.
builder.Services.PostConfigure<ToamaisutaaOidcOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.PublicUrl))
        options.PublicUrl = builder.Configuration["Krint:PublicUrl"];
});

// The anonymous endpoints (SPA configuration, health) are the only ones reachable without a
// token, so they are the only ones a stranger can hammer. Per caller address, generous enough
// that a dashboard reloading in a loop never notices.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(SecurityHeaders.AnonymousPolicy, context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
            }));
});

var app = builder.Build();

// Behind a TLS-terminating proxy the scheme and host KRINT sees are the proxy's, which breaks
// the derived login redirect (http:// where the IdP expects https://). Opt-in, because trusting
// these headers from just anyone lets a direct caller spoof its address and scheme.
if (builder.Configuration.GetValue("Krint:TrustForwardedHeaders", false))
{
    var forwarded = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
    };
    forwarded.KnownNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();

app.UseSecurityHeaders(builder.Configuration["Oidc:Authority"]);

app.ApplyMigrations();
await app.ApplySeedsAsync();

// The document is what a client generator or a curious integrator reads first, so it is served
// everywhere; the interactive reference stays a development tool.
app.MapOpenApi().AllowAnonymous().RequireRateLimiting(SecurityHeaders.AnonymousPolicy);

if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options =>
    {
        if (localLogin)
        {
            options.AddPreferredSecuritySchemes(OAuth2SecuritySchemeTransformer.BearerSchemeName);
        }
        else
        {
            options
                .AddPreferredSecuritySchemes(OAuth2SecuritySchemeTransformer.OAuth2SchemeName)
                .AddAuthorizationCodeFlow(OAuth2SecuritySchemeTransformer.OAuth2SchemeName, flow =>
                {
                    flow.ClientId = builder.Configuration["Oidc:ClientId"];
                    flow.Pkce = Pkce.Sha256;
                    flow.SelectedScopes = ["openid", "profile", "email", "roles"];
                });
        }
    }).AllowAnonymous();
}

app.UseStaticFiles();

if (app.Environment.IsProduction())
    app.UseSpaStaticFiles();

app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Surface node-routing failures as clean 4xx instead of opaque 500s: an offline node is a transient
// conflict; an unsupported-on-node operation is a bad request. Hubs handle their own errors, so this
// only ever fires for controller calls (response not yet started).
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (NodeOfflineException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (NotSupportedException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

app.MapControllers();
app.MapKrintHealth();
if (localLogin)
    app.MapKrintLocalLogin();
app.MapHub<ContainerHub>("/hubs/container").RequireAuthorization();
app.MapHub<DashboardHub>("/hubs/dashboard").RequireAuthorization();
app.MapHub<MigrationHub>("/hubs/migration").RequireAuthorization();
app.MapHub<ProvisionHub>("/hubs/provision").RequireAuthorization();
// Nodes authenticate with a pre-shared token inside the hub, so no OIDC authorization here.
app.MapHub<NodeHub>("/hubs/node");

if (app.Environment.IsProduction())
    app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

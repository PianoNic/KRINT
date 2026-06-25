using KRINT.API;
using KRINT.API.Extensions;
using KRINT.API.Hubs;
using KRINT.API.Nodes;
using KRINT.API.OpenApi;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

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
    builder.Services.AddHostedService<NodeAgentHostedService>();

    var nodeApp = builder.Build();
    nodeApp.MapGet("/health", () => Results.Ok(new { status = "ok", role = "node" }));
    nodeApp.Run();
    return;
}

builder.Services.AddKrintConfig(builder.Environment);

builder.Services.AddSpaStaticFiles(options => { options.RootPath = "wwwroot"; });

builder.Services.AddControllers();
builder.Services.AddSignalR(options =>
{
    // Container output can burst over the default 32 KB cap when a server logs verbose startup
    // or a user runs ls in a huge directory. Node RPC also returns whole backup dumps as one
    // message. Lift the cap (dumps are already fully buffered in memory) so neither gets axed.
    options.MaximumReceiveMessageSize = null;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<OAuth2SecuritySchemeTransformer>();
});

builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });
builder.Services.AddScoped<KRINT.Application.ConfigManagedGuard>();

builder.Services.AddKrintDatabase(builder.Configuration);

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

// Defaults to no cross-origin allowlist when unset. The desktop build serves the SPA
// same-origin from the sidecar, so it needs none; server deployments set it explicitly.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var publicAuthority = builder.Configuration["Oidc:Authority"];
        var internalAuthority = builder.Configuration["Oidc:InternalAuthority"] ?? publicAuthority;
        options.MetadataAddress = $"{internalAuthority!.TrimEnd('/')}/.well-known/openid-configuration";
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Oidc:RequireHttpsMetadata", true);
        options.TokenValidationParameters.ValidIssuer = publicAuthority;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";
        options.TokenValidationParameters.ValidateAudience = false;

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                // /hubs/node carries a pre-shared node token, not an OIDC JWT - it authenticates inside
                // the hub, so keep it out of JWT validation here.
                if (!string.IsNullOrEmpty(accessToken)
                    && context.Request.Path.StartsWithSegments("/hubs")
                    && !context.Request.Path.StartsWithSegments("/hubs/node"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

app.ApplyMigrations();
await app.ApplySeedsAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options =>
    {
        options
            .AddPreferredSecuritySchemes("OAuth2")
            .AddAuthorizationCodeFlow("OAuth2", flow =>
            {
                flow.ClientId = builder.Configuration["Oidc:ClientId"];
                flow.Pkce = Pkce.Sha256;
                flow.SelectedScopes = ["openid", "profile", "email", "roles"];
            });
    }).AllowAnonymous();
}

app.UseStaticFiles();

if (app.Environment.IsProduction())
    app.UseSpaStaticFiles();

app.UseRouting();
app.UseCors();
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
app.MapHub<ContainerHub>("/hubs/container").RequireAuthorization();
app.MapHub<DashboardHub>("/hubs/dashboard").RequireAuthorization();
app.MapHub<MigrationHub>("/hubs/migration").RequireAuthorization();
// Nodes authenticate with a pre-shared token inside the hub, so no OIDC authorization here.
app.MapHub<NodeHub>("/hubs/node");

if (app.Environment.IsProduction())
    app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

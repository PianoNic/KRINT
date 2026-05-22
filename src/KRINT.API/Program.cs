using KRINT.API.Extensions;
using KRINT.API.OpenApi;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKrintConfig(builder.Environment);

// When the Angular dist is baked into the image (production), serve it from wwwroot/browser
// (Angular CLI's default output path). In dev we don't add this - the frontend runs separately
// on its own port. The RootPath is only consulted in production.
builder.Services.AddSpaStaticFiles(opts => { opts.RootPath = "wwwroot/browser"; });

builder.Services.AddControllers();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<OAuth2SecuritySchemeTransformer>();
});

builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });

builder.Services.AddDbContext<KrintDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("KrintDatabase"));
    // EF tools 10.0.7 + runtime 10.0.8 disagree on the model snapshot fingerprint even when
    // the actual schema is in sync. Demote the warning so a tool-version skew can't crash
    // app boot; real pending changes still surface through the migrations pipeline.
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

builder.Services.AddDocker(builder.Configuration);

builder.Services.AddSecrets();

builder.Services.AddInnerDatabases();

builder.Services.AddCatalog();

builder.Services.AddHostedService<KRINT.API.BackupSchedulerHostedService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? throw new InvalidOperationException("Cors:AllowedOrigins not configured");

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Oidc:Authority"];
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Oidc:RequireHttpsMetadata", true);
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";
        options.TokenValidationParameters.ValidateAudience = false;
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

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Serve the bundled Angular SPA. In dev (no wwwroot/browser) this is a no-op; in the
// bundled image the multi-stage Dockerfile copies the dist into wwwroot/browser and any
// non-API path falls through to index.html so client-side routing works.
if (!app.Environment.IsDevelopment())
{
    app.UseStaticFiles();
    app.UseSpaStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.Run();

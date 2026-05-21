using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using KRINT.API.Extensions;
using KRINT.API.OpenApi;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });

builder.Services.AddDbContext<KrintDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("KrintDatabase")));

builder.Services.AddDocker(builder.Configuration);

builder.Services.AddSecrets();

builder.Services.AddCatalog();

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
}

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

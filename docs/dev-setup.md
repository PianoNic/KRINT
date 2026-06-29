# KRINT Developer Setup

This is what a fresh checkout needs to be productive locally.

## Prerequisites

- **.NET 10 SDK** (the API targets `net10.0`)
- **Docker** + **Docker Compose** (for Postgres and Keycloak)
- **Node.js 20+** and **Bun 1.3+** (the frontend uses bun as its package manager)
- **Apache Maven** (only if you want to rebuild the Keycloak theme JAR, see Notes)
- **dotnet-ef** global tool (only if you'll add EF migrations)

## 1. Backend secrets

Secrets live in **dotnet user-secrets**: they are never committed and never written to `appsettings.json`. The `KRINT.API` project has a `UserSecretsId` configured.

Set them once:

```powershell
# DB connection
dotnet user-secrets --project src/KRINT.API set "ConnectionStrings:KrintDatabase" "Host=localhost;Port=5434;Database=krint-dev;Username=postgres;Password=d4vpas8w0rd13!!!"

# OIDC (consumed by /api/App, which the frontend reads at startup to configure auth)
dotnet user-secrets --project src/KRINT.API set "Oidc:Authority" "http://localhost:8080/realms/krint"
dotnet user-secrets --project src/KRINT.API set "Oidc:RequireHttpsMetadata" "false"
dotnet user-secrets --project src/KRINT.API set "Oidc:ClientId" "krint"
dotnet user-secrets --project src/KRINT.API set "Oidc:RedirectUri" "http://localhost:4200/"
dotnet user-secrets --project src/KRINT.API set "Oidc:PostLogoutRedirectUri" "http://localhost:4200/"
dotnet user-secrets --project src/KRINT.API set "Oidc:Scope" "openid profile email roles"

# CORS: origins allowed to call the API
dotnet user-secrets --project src/KRINT.API set "Cors:AllowedOrigins:0" "http://localhost:4200"

# Vault master key, used by SecretsVaultService to AES-GCM encrypt stored secrets at rest.
# 32 raw bytes, base64-encoded. Generate fresh on bash via `openssl rand -base64 32`.
dotnet user-secrets --project src/KRINT.API set "Vault:MasterKey" "$(openssl rand -base64 32)"
```

Verify with `dotnet user-secrets list --project src/KRINT.API`.

::: info
`appsettings.json` and `appsettings.Development.json` only carry ASP.NET framework defaults (logging, allowed hosts). Application config goes in user-secrets.
:::

::: warning
**Don't rotate `Vault:MasterKey` while you have encrypted secrets in the DB**: anything written with the old key becomes unreadable. There's no key-rotation flow yet.
:::

## 2. Application config: `krint.yaml`

Non-secret app config lives in **`krint.yaml`** at the repo root. The API loads it via `services.AddKrintConfig(env)` (see `src/KRINT.API/Extensions/KrintConfigExtensions.cs`), which walks up from the content root to find the file. Override the path with the `KRINT_CONFIG` environment variable.

Currently used to declare which host ports each engine is allowed to bind:

```yaml
krint:
  port_ranges:
    postgres: 30000-30199
    mysql:    30200-30399
    mariadb:  30400-30599
    mssql:    30600-30799
    mongo:    30800-30999
```

Bind into a handler via `IOptions<KrintOptions>` (in `KRINT.Application/Options/`). The file is reload-on-change, so edits are picked up without a restart.

## 3. Dev infrastructure (Postgres + Keycloak)

```powershell
docker compose -f compose.dev.yml up -d
```

- **Postgres** → `localhost:5434`, db `krint-dev`, user `postgres`, password `d4vpas8w0rd13!!!`
- **Keycloak** → `http://localhost:8080`, admin `admin` / `admin`. The `krint` realm is auto-imported on first start from `keycloak/krint-realm.json`.

Stop with `docker compose -f compose.dev.yml down`. Volumes (`postgres-data-dev`, `keycloak-data-dev`) persist across restarts; drop them with `-v` if you want a clean slate.

## 4. Backend

```powershell
dotnet run --project src/KRINT.API --launch-profile http
```

The API binds to `http://localhost:5165`. In Development:
- OpenAPI document: `http://localhost:5165/openapi/v1.json` (`AllowAnonymous`)
- Scalar API reference UI: `http://localhost:5165/scalar/v1`: click **Authenticate** to redirect to Keycloak (authorization code + PKCE against the `krint` realm/client). On return, Scalar attaches the bearer token to every request you fire from the UI.

On startup the API:
1. Applies any pending EF migrations to the dev DB (`ApplyMigrations()`).
2. Runs seeders (`ApplySeedsAsync()`: currently a no-op placeholder).
3. Talks to the local **Docker socket** via `DockerService` (Windows named pipe / Unix socket auto-detected by Docker.DotNet).

## 5. Frontend: first run

```powershell
cd src/KRINT.Frontend
bun install
```

With the backend running, generate the typed API client:

```powershell
bun run apigen
```

This reads `openapitools.json`, fetches `http://localhost:5165/openapi/v1.json`, and writes the `typescript-angular` client into `src/app/api/`. Rerun any time the backend's contract changes.

Then start the dev server:

```powershell
bun start
```

Frontend on `http://localhost:4200`.

## 6. Tests

```powershell
dotnet run --project src/KRINT.Tests
```

TUnit 1.x test runner. Coverage:

- **Unit**: `PingQueryTests`, `SecretGeneratorServiceTests`, `SecretsVaultServiceTests`. All use `Microsoft.EntityFrameworkCore.InMemory`: no Postgres required.
- **E2E**: Browser-driven tests via Microsoft.Playwright. The suite boots its **own** ephemeral stack each session by driving `e2e/compose.e2e.yml` through Ductus.FluentDocker (Postgres + `mock-oauth2-server` + the bundled KRINT image built from this repo's Dockerfile). You don't need to run the dev stack manually. Spin it up by hand for debugging with `docker compose -f e2e/compose.e2e.yml up -d --build`.

### Running the E2E suite

Prereqs: Docker running on the host, plus (one-time) Playwright's Chromium build:

```powershell
dotnet build src/KRINT.Tests
pwsh src/KRINT.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Then:

```powershell
dotnet run --project src/KRINT.Tests
```

What happens on first run:
1. **Image build** (~2 min): compose builds `src/KRINT.API/Dockerfile` (frontend via `bun build` + .NET API publish) into a single distroless Azure Linux image.
2. **Stack boot** (~30 s): Postgres 18 on `localhost:15434`, `mock-oauth2-server` on `localhost:18080` (non-interactive, auto-issues signed JWTs from `e2e/mock-oauth2-config.json`), KRINT app on `localhost:18081`.
3. **Browser tests**: each test opens a fresh browser context. The SPA redirects to the mock IdP, which auto-redirects straight back with a token. No login form, no credentials.

Test files (`src/KRINT.Tests/E2E/`):
- `KrintStack.cs`: drives `e2e/compose.e2e.yml` via Ductus.FluentDocker, exposes URLs
- `KrintTestFixture.cs`: per-test browser context
- `KrintSessionHooks.cs`: TUnit `[Before/After(TestSession)]` for stack lifecycle
- `WizardHelper.cs`: drives the `/create` wizard
- `NavigationTests`, `WizardTests`, `InstanceDialogTests`, `BackupTests`, `ActivityLogTests`, `InstanceLifecycleTests`

Headless by default. Set `KrintTestFixture.Headless = false` in `KrintSessionHooks.StartStack` for visible browser windows during local debugging.

## 7. EF migrations

KRINT can store its own data in **SQLite** (default) or **PostgreSQL**, selected with
`Database__Provider` (`Sqlite` or `Postgres`). The connection string is
`ConnectionStrings__KrintDatabase`; for SQLite it defaults to `Data Source=krint.db`.

Because EF Core can't keep two providers' migrations in one assembly, there are **two**
migration sets, both auto-applied at API startup via `ApplyMigrations()` for whichever
provider is active:

- **Postgres** → `src/KRINT.Infrastructure/Migrations/`
- **SQLite** → `src/KRINT.Infrastructure.Migrations.Sqlite/Migrations/`

After changing entities, add the migration to **both** sets (the EF tooling reads the
target provider from `Database__Provider`):

```powershell
# Postgres
$env:Database__Provider="Postgres"
dotnet ef migrations add <Name> -p src/KRINT.Infrastructure -s src/KRINT.API

# SQLite
$env:Database__Provider="Sqlite"
dotnet ef migrations add <Name> -p src/KRINT.Infrastructure.Migrations.Sqlite -s src/KRINT.API
```

`dotnet ef database update` is **not** needed for local dev. `dotnet run` does it on startup. Run it manually only if you want to apply migrations without booting the API.

## Notes

- **Keycloak theme**: `keycloak/krint-realm.json` references `loginTheme: krint`. The theme source lives in `keycloak/keycloakify/`. Build the JAR with `bun run build-keycloak-theme` inside `keycloak/keycloakify/` (Apache Maven must be on `PATH`); the dev compose mounts the JAR into Keycloak. Until you build it, dev Keycloak falls back to the default theme.
- **API exploration**: use the Scalar UI at `/scalar/v1`, the generated `typescript-angular` client, or any spec-aware tool against `/openapi/v1.json`. No Swagger UI is mounted.
- **Scalar auth wiring**: `OAuth2SecuritySchemeTransformer` declares an OAuth2 authorization-code scheme on the OpenAPI doc (derived from `Oidc:Authority`); `MapScalarApiReference` pre-fills `ClientId` + PKCE. The `krint` Keycloak client whitelists `http://localhost:5165/scalar/v1/oauth2-redirect.html` as a valid redirect URI. If you add another API origin or path, update `keycloak/krint-realm.json` and re-import the realm (drop the `keycloak-data-dev` volume).
- **Docker socket**: `DockerService` connects to the local Docker daemon. On Windows this is the Docker Desktop named pipe `npipe://./pipe/docker_engine`; on Linux it's `unix:///var/run/docker.sock`. When the API runs *inside* a container (prod compose), the host's socket must be mounted into it (`/var/run/docker.sock:/var/run/docker.sock`).
- **Architecture quick map**:
  - `KRINT.Domain`: entities (`Secret`, `BaseEntity`), no dependencies
  - `KRINT.Application`: Mediator queries/commands + DTOs
  - `KRINT.Infrastructure`: EF DbContext + migrations + `Services/` (DockerService, SecretGeneratorService, SecretsVaultService) + `Interfaces/`
  - `KRINT.API`: ASP.NET Core entry point, JWT bearer auth, CORS, OpenAPI, controllers, DI wiring via `AddDocker()` / `AddSecrets()` / etc.
  - `KRINT.Frontend`: Angular 21 + Tailwind + Spartan UI, OIDC via `angular-auth-oidc-client`, typed API client generated by openapi-generator-cli.

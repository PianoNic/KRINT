# KRINT — Developer Setup

This is what a fresh checkout needs to be productive locally.

## Prerequisites

- **.NET 10 SDK** (the API targets `net10.0`)
- **Docker** + **Docker Compose** (for Postgres and Keycloak)
- **Node.js 20+** and **Bun 1.3+** (the frontend uses bun as its package manager)
- **Apache Maven** (only if you want to rebuild the Keycloak theme JAR — see Notes)
- **dotnet-ef** global tool (only if you'll add EF migrations)

## 1. Backend secrets

Secrets live in **dotnet user-secrets** — they are never committed and never written to `appsettings.json`. The `KRINT.API` project has a `UserSecretsId` configured.

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

# CORS — origins allowed to call the API
dotnet user-secrets --project src/KRINT.API set "Cors:AllowedOrigins:0" "http://localhost:4200"

# Vault master key — used by SecretsVaultService to AES-GCM encrypt stored secrets at rest.
# 32 raw bytes, base64-encoded. Generate fresh on bash via `openssl rand -base64 32`.
dotnet user-secrets --project src/KRINT.API set "Vault:MasterKey" "$(openssl rand -base64 32)"
```

Verify with `dotnet user-secrets list --project src/KRINT.API`.

> `appsettings.json` and `appsettings.Development.json` only carry ASP.NET framework defaults (logging, allowed hosts). Application config goes in user-secrets.

> **Don't rotate `Vault:MasterKey` while you have encrypted secrets in the DB** — anything written with the old key becomes unreadable. There's no key-rotation flow yet.

## 2. Dev infrastructure (Postgres + Keycloak)

```powershell
docker compose -f compose.dev.yml up -d
```

- **Postgres** → `localhost:5434`, db `krint-dev`, user `postgres`, password `d4vpas8w0rd13!!!`
- **Keycloak** → `http://localhost:8080`, admin `admin` / `admin`. The `krint` realm is auto-imported on first start from `keycloak/krint-realm.json`.

Stop with `docker compose -f compose.dev.yml down`. Volumes (`postgres-data-dev`, `keycloak-data-dev`) persist across restarts; drop them with `-v` if you want a clean slate.

## 3. Backend

```powershell
dotnet run --project src/KRINT.API --launch-profile http
```

The API binds to `http://localhost:5165`. OpenAPI document: `http://localhost:5165/openapi/v1.json` (Development only, `AllowAnonymous`).

On startup the API:
1. Applies any pending EF migrations to the dev DB (`ApplyMigrations()`).
2. Runs seeders (`ApplySeedsAsync()` — currently a no-op placeholder).
3. Talks to the local **Docker socket** via `DockerService` (Windows named pipe / Unix socket auto-detected by Docker.DotNet).

## 4. Frontend — first run

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

## 5. Tests

```powershell
dotnet run --project src/KRINT.Tests
```

TUnit 1.x test runner. Current coverage:

- `PingQueryTests` — Mediator query handler smoke test
- `SecretGeneratorServiceTests` — length, alphabet, uniqueness
- `SecretsVaultServiceTests` — encrypt/decrypt round-trip, tamper-detection (AES-GCM auth tag), missing/invalid master-key, store + delete behavior

Vault tests use `Microsoft.EntityFrameworkCore.InMemory` so they don't need a running Postgres.

## 6. EF migrations

Migrations live in `src/KRINT.Infrastructure/Migrations/` and are auto-applied at API startup via `ApplyMigrations()`. To add a new one after changing entities:

```powershell
dotnet ef migrations add <Name> -p src/KRINT.Infrastructure -s src/KRINT.API
```

`dotnet ef database update` is **not** needed for local dev — `dotnet run` does it on startup. Run it manually only if you want to apply migrations without booting the API.

## Notes

- **Keycloak theme**: `keycloak/krint-realm.json` references `loginTheme: krint`. The theme source lives in `keycloak/keycloakify/`. Build the JAR with `bun run build-keycloak-theme` inside `keycloak/keycloakify/` (Apache Maven must be on `PATH`); the dev compose mounts the JAR into Keycloak. Until you build it, dev Keycloak falls back to the default theme.
- **No Swagger UI**: the API exposes only the raw OpenAPI JSON. Use the generated `typescript-angular` client, Postman, or any spec-aware tool to explore.
- **Docker socket**: `DockerService` connects to the local Docker daemon. On Windows this is the Docker Desktop named pipe `npipe://./pipe/docker_engine`; on Linux it's `unix:///var/run/docker.sock`. When the API runs *inside* a container (prod compose), the host's socket must be mounted into it (`/var/run/docker.sock:/var/run/docker.sock`).
- **Architecture quick map**:
  - `KRINT.Domain` — entities (`Secret`, `BaseEntity`), no dependencies
  - `KRINT.Application` — Mediator queries/commands + DTOs
  - `KRINT.Infrastructure` — EF DbContext + migrations + `Services/` (DockerService, SecretGeneratorService, SecretsVaultService) + `Interfaces/`
  - `KRINT.API` — ASP.NET Core entry point, JWT bearer auth, CORS, OpenAPI, controllers, DI wiring via `AddDocker()` / `AddSecrets()` / etc.
  - `KRINT.Frontend` — Angular 21 + Tailwind + Spartan UI, OIDC via `angular-auth-oidc-client`, typed API client generated by openapi-generator-cli.

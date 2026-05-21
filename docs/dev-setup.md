# KRINT — Developer Setup

This is what a fresh checkout needs to be productive locally.

## Prerequisites

- **.NET 10 SDK** (the API targets `net10.0`)
- **Docker** + **Docker Compose** (for Postgres and Keycloak)
- **Node.js 20+** and **Bun 1.3+** (the frontend uses bun as its package manager)

## 1. Backend secrets

Secrets live in **dotnet user-secrets** — they are never committed and never written to `appsettings.json`. The `KRINT.API` project has a `UserSecretsId` configured.

Set them once:

```powershell
dotnet user-secrets --project src/KRINT.API set "ConnectionStrings:KrintDatabase" "Host=localhost;Port=5434;Database=krint-dev;Username=postgres;Password=d4vpas8w0rd13!!!"
dotnet user-secrets --project src/KRINT.API set "Oidc:Authority" "http://localhost:8080/realms/krint"
dotnet user-secrets --project src/KRINT.API set "Oidc:RequireHttpsMetadata" "false"
dotnet user-secrets --project src/KRINT.API set "Oidc:ClientId" "krint"
dotnet user-secrets --project src/KRINT.API set "Oidc:RedirectUri" "http://localhost:4200/"
dotnet user-secrets --project src/KRINT.API set "Oidc:PostLogoutRedirectUri" "http://localhost:4200/"
dotnet user-secrets --project src/KRINT.API set "Oidc:Scope" "openid profile email roles"
```

Verify with `dotnet user-secrets list --project src/KRINT.API`.

> `appsettings.json` and `appsettings.Development.json` only carry ASP.NET framework defaults (logging, allowed hosts). Application config goes in user-secrets.

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

The API binds to `http://localhost:5165`. OpenAPI document: `http://localhost:5165/openapi/v1.json` (Development only).

## 4. Frontend — first run

```powershell
cd src/KRINT.Frontend
bun install
```

With the backend running, generate the typed API client:

```powershell
bun run apigen
```

This reads `openapitools.json`, fetches the OpenAPI document, and writes the `typescript-angular` client into `src/app/api/`. Rerun any time the backend's contract changes.

Then start the dev server:

```powershell
bun start
```

Frontend on `http://localhost:4200`.

## Notes

- **Keycloak theme**: `keycloak/krint-realm.json` references `loginTheme: krint`. The theme source lives in `keycloak/keycloakify/`. Build the JAR with `bun run build-keycloak-theme` inside `keycloak/keycloakify/`; the prod compose mounts the JAR into Keycloak. Until you build it, dev Keycloak falls back to the default theme.
- **No swagger UI**: the API exposes only the raw OpenAPI JSON. Use Postman, the OpenAPI client itself, or any spec-aware tool to explore.
- **Database migrations**: not configured yet. When you add the first migration, run `dotnet ef migrations add Init -p src/KRINT.Infrastructure -s src/KRINT.API` and apply with `dotnet ef database update`.

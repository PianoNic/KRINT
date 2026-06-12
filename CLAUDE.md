# KRINT — working conventions

## Workflow (enforced)

Never work on main. Always:
1. `gh issue create` (with a label)
2. Branch `feature/<issue#>_PascalCase` or `fix/<issue#>_PascalCase`
3. `gh pr create` (with a label) — body is Summary + `Closes #<issue>` only
4. Squash-merge + delete branch

## Commits

- Short imperative subject line.
- No AI / Claude attribution. No `Co-Authored-By`, no `🤖 Generated with...`, nothing.

## PRs

- Title mirrors the commit / issue.
- Body: 1–2 sentence summary + `Closes #<issue>`. No test plans, no checklists, no headers.
- Labels: `bug`, `enhancement`, `refactor`, `stale`.

## CLI generators

Use them whenever one exists — `gh issue create`, `gh pr create`, `npx create-expo-app`, `npx expo install`, etc.

## Local dev setup

- `compose.yml` runs KRINT with `mock-oauth2-server` (no Keycloak) on random ephemeral ports.
- Ports are fixed in `compose.yml` — edit them if they conflict.
- `Vault__MasterKey` must be set in the `krint` service environment (see compose.yml).
- The runtime image is `mcr.microsoft.com/dotnet/aspnet:10.0.x-azurelinux3.0` (non-distroless) — required for ICU/globalization support (MSSQL client).
- `localhost`/`127.0.0.1` in register/probe calls is rewritten to `host.docker.internal` inside the container.

## Migrations

After pulling upstream, run:
```
dotnet ef migrations has-pending-model-changes --project src/KRINT.Infrastructure --startup-project src/KRINT.API
```
If pending, remove any stale local migration and regenerate:
```
dotnet ef migrations remove --project src/KRINT.Infrastructure --startup-project src/KRINT.API --force
dotnet ef migrations add <Name> --project src/KRINT.Infrastructure --startup-project src/KRINT.API
```
Then rebuild the image.

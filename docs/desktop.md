# KRINT desktop app

KRINT ships two ways from one codebase:

| Distribution | Database | Auth | Use case |
| ------------ | -------- | ---- | -------- |
| **Docker image** (`docs/self-host.md`) | PostgreSQL | Keycloak (OIDC) | Servers, multi-user, always-on |
| **Desktop app** (this doc) | SQLite | bundled mock OIDC | Single user on their own machine |

The desktop build is a [Tauri v2](https://v2.tauri.app) window wrapped around the **same
`KRINT.API` binary** the Docker image runs. The API serves the SPA + its OIDC config itself
(Production `MapFallbackToFile`), so the webview just points at the local backend — no separate
frontend build, no API changes.

## How it works

On launch the desktop shell (`src-tauri/src/lib.rs`):

1. Creates an app-data dir and a stable `vault.key` (AES-256, generated once, reused).
2. Starts the **mock OIDC server** (`ghcr.io/navikt/mock-oauth2-server`, `interactiveLogin: false`)
   as a Docker container, using the same config as the e2e tests — sign-in is automatic.
3. Spawns `KRINT.API` as a **sidecar** with `Database__Provider=Sqlite` and the SQLite file in
   the app-data dir.
4. Waits for the API to log `Application started`, then navigates the window to
   `http://127.0.0.1:5111/`.
5. On exit, kills the API child and removes the mock OIDC container.

### Why Docker is still required

KRINT provisions database instances as **sibling Docker containers**, so the desktop app — like
the server — needs a Docker engine (Docker Desktop on Windows/macOS) running on the host. The
desktop app reuses that same engine to run the mock OIDC container, so there's no extra
dependency beyond Docker itself.

## Prerequisites

- [Rust](https://www.rust-lang.org/tools/install) + the Tauri system deps for your OS
  (see <https://v2.tauri.app/start/prerequisites/>).
- The [.NET 10 SDK](https://dotnet.microsoft.com/download).
- Node.js (for the Tauri CLI and the sidecar publish script).
- Docker Desktop / a running Docker engine.

Install JS deps and the Tauri CLI once:

```bash
npm install
```

## Build & run

The sidecar (`KRINT.API`) is published self-contained and copied into `src-tauri/binaries/` with
the target-triple suffix Tauri expects. This runs automatically via `beforeBuildCommand`, or
manually:

```bash
npm run publish:sidecar          # publishes for the host triple
# cross-publish a specific runtime:
RID=osx-arm64 npm run publish:sidecar
```

Then:

```bash
npm run desktop:dev              # dev window
npm run desktop:build            # installers under src-tauri/target/release/bundle/
```

> First run also needs app icons. Generate them once with `npm run tauri icon path/to/icon.png`
> (writes into `src-tauri/icons/`, which is gitignored).

### .NET RID ↔ Tauri target triple

| Platform | .NET RID | Tauri triple suffix |
| -------- | -------- | ------------------- |
| Windows x64 | `win-x64` | `x86_64-pc-windows-msvc` |
| macOS Apple Silicon | `osx-arm64` | `aarch64-apple-darwin` |
| macOS Intel | `osx-x64` | `x86_64-apple-darwin` |
| Linux x64 | `linux-x64` | `x86_64-unknown-linux-gnu` |

Find the host triple with `rustc --print host-tuple`.

## Notes / TODO

- **Keep ICU**: the sidecar is published *without* `InvariantGlobalization` because the MSSQL
  client needs ICU. Single-file self-contained bundles ICU by default.
- **Ports** (`5111` API, `18080` OIDC) are currently fixed in `src-tauri/src/lib.rs`. Binding the
  API to port `0` and parsing the chosen port from stdout would avoid collisions — a good
  follow-up.
- The desktop SQLite database lives in the OS app-data dir (e.g. `%APPDATA%/app.krint.desktop`,
  `~/Library/Application Support/app.krint.desktop`, `~/.local/share/app.krint.desktop`).

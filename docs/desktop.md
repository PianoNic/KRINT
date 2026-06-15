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
2. Starts a tiny **in-process OIDC issuer** (`src-tauri/src/oidc.rs`) that auto-issues tokens
   (no login screen) — zero-config local sign-in, no Docker or Java needed.
3. Spawns `KRINT.API` as a **sidecar** with `Database__Provider=Sqlite` and the SQLite file in
   the app-data dir.
4. Waits for the API to log `Application started`, then navigates the window to
   `http://127.0.0.1:5111/`.
5. On exit, kills the API child; the in-process OIDC issuer stops with the app.

### Why Docker is still required

KRINT provisions database instances as **sibling Docker containers**, so the desktop app — like
the server — needs a Docker engine (Docker Desktop on Windows/macOS) running on the host. Auth,
however, no longer needs Docker: it's the in-process issuer above.

## Prerequisites

- [Rust](https://www.rust-lang.org/tools/install) + the Tauri system deps for your OS
  (see <https://v2.tauri.app/start/prerequisites/>).
- The [.NET 10 SDK](https://dotnet.microsoft.com/download).
- [Bun](https://bun.sh) (frontend build) and Node.js (runs the sidecar publish script).
- Docker Desktop / a running Docker engine (for provisioning database instances).

Install JS deps and the Tauri CLI once:

```bash
bun install
```

## Build & run

The sidecar (`KRINT.API`) is published self-contained and copied into `src-tauri/binaries/` with
the target-triple suffix Tauri expects. This runs automatically via `beforeBuildCommand`, or
manually:

```bash
bun run publish:sidecar          # publishes for the host triple
# cross-publish a specific runtime:
RID=osx-arm64 bun run publish:sidecar
```

Then:

```bash
bun run desktop:dev              # dev window
bun run desktop:build            # installers under src-tauri/target/release/bundle/
```

> First run also needs app icons. Generate them once with `bun run tauri icon path/to/icon.png`
> (writes into `src-tauri/icons/`, which is gitignored).

### .NET RID ↔ Tauri target triple

| Platform | .NET RID | Tauri triple suffix |
| -------- | -------- | ------------------- |
| Windows x64 | `win-x64` | `x86_64-pc-windows-msvc` |
| macOS Apple Silicon | `osx-arm64` | `aarch64-apple-darwin` |
| macOS Intel | `osx-x64` | `x86_64-apple-darwin` |
| Linux x64 | `linux-x64` | `x86_64-unknown-linux-gnu` |

Find the host triple with `rustc --print host-tuple`.

## Auto-updates

The app checks for updates on launch (`tauri-plugin-updater`). If a newer signed release is
available it downloads, installs, and restarts — replacing the whole bundle (API sidecar +
resources included), so one update covers everything.

How it fits together:
- `tauri.conf.json` → `plugins.updater.endpoints` points at
  `https://github.com/PianoNic/KRINT/releases/latest/download/latest.json`, and `pubkey` holds
  the public signing key.
- `bundle.createUpdaterArtifacts: true` makes `tauri build` emit signed updater bundles + `.sig`.
- The release workflow signs them and uploads `latest.json` (the update manifest).
- Windows ships the **NSIS `.exe`** installer only (per-user, x64 + arm64, best updater support);
  MSI is intentionally not built.
- Updatable formats: **AppImage** (Linux), **NSIS `.exe`** (Windows), **.app** (macOS). `.deb`/`.rpm`
  are install-only (no self-update) — that's a Tauri limitation, not ours.

### One-time setup (required before releases self-update)

1. Generate a signing keypair:
   ```bash
   bunx tauri signer generate -w ~/.tauri/krint-updater.key
   ```
2. Put the **public** key into `tauri.conf.json` → `plugins.updater.pubkey` (replaces the
   `REPLACE_WITH_...` placeholder). The public key is safe to commit.
3. Add the **private** key + its password as GitHub Actions secrets (never commit them):
   - `TAURI_SIGNING_PRIVATE_KEY`
   - `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`

Until the real pubkey + secrets are in place, signed builds (`tauri build`) and auto-update won't
work — the rest of the desktop build still does.

## Notes / TODO

- **Keep ICU**: the sidecar is published *without* `InvariantGlobalization` because the MSSQL
  client needs ICU. Single-file self-contained bundles ICU by default.
- **Ports** (`5111` API, `18080` OIDC) are currently fixed in `src-tauri/src/lib.rs`. Binding the
  API to port `0` and parsing the chosen port from stdout would avoid collisions — a good
  follow-up.
- The desktop SQLite database lives in the OS app-data dir (e.g. `%APPDATA%/app.krint.desktop`,
  `~/Library/Application Support/app.krint.desktop`, `~/.local/share/app.krint.desktop`).

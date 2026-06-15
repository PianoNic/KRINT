// KRINT desktop shell.
//
// The desktop app is a thin Tauri window around the same `KRINT.API` binary the Docker
// image runs. On startup we:
//   1. ensure a stable vault key + SQLite path under the OS app-data dir,
//   2. start a tiny in-process OIDC issuer (zero-config local sign-in, no Docker/Java),
//   3. spawn `KRINT.API` as a sidecar configured for SQLite,
//   4. wait for it to report "Application started", then point the window at it.
//
// The API serves the SPA + OIDC config itself (Production `MapFallbackToFile`), so the
// webview talks to a single local origin exactly like the Docker deployment.

mod oidc;

use std::path::PathBuf;
use std::sync::Mutex;

use base64::Engine;
use tauri::{Manager, RunEvent};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;

// Fixed local ports. TODO: bind the API to port 0 and parse the actual port from its
// stdout so two instances can't collide.
const API_PORT: u16 = 5111;
const OIDC_PORT: u16 = 18080;
const CLIENT_ID: &str = "krint";

/// Holds the running API child so we can terminate it on shutdown.
struct Backend(Mutex<Option<CommandChild>>);

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(Backend(Mutex::new(None)))
        .setup(|app| {
            let handle = app.handle().clone();
            if let Err(err) = start_backend(&handle) {
                eprintln!("[krint] failed to start backend: {err}");
            }
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building KRINT desktop app")
        .run(|app, event| {
            // Tear the API sidecar down when the app exits. The OIDC issuer is in-process
            // and stops automatically with the app.
            if let RunEvent::ExitRequested { .. } = event {
                if let Some(child) = app.state::<Backend>().0.lock().unwrap().take() {
                    let _ = child.kill();
                }
            }
        });
}

fn start_backend(app: &tauri::AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let data_dir = app.path().app_data_dir()?;
    std::fs::create_dir_all(&data_dir)?;

    let db_path = data_dir.join("krint.db");
    let vault_key = load_or_create_vault_key(&data_dir)?;

    let authority = format!("http://localhost:{OIDC_PORT}");
    start_oidc(authority.clone());
    let urls = format!("http://127.0.0.1:{API_PORT}");
    let conn = format!("Data Source={}", db_path.to_string_lossy());
    let krint_yaml = app
        .path()
        .resolve("resources/krint.yaml", tauri::path::BaseDirectory::Resource)?;

    let sidecar = app
        .shell()
        .sidecar("binaries/krint-api")?
        .env("ASPNETCORE_ENVIRONMENT", "Production")
        .env("ASPNETCORE_URLS", &urls)
        .env("Database__Provider", "Sqlite")
        .env("ConnectionStrings__KrintDatabase", &conn)
        .env("KRINT_CONFIG", krint_yaml.to_string_lossy().to_string())
        .env("Vault__MasterKey", vault_key)
        .env("Oidc__Authority", &authority)
        .env("Oidc__InternalAuthority", &authority)
        .env("Oidc__RequireHttpsMetadata", "false")
        .env("Oidc__ClientId", CLIENT_ID)
        .env("Oidc__Scope", "openid profile email roles")
        .env("Cors__AllowedOrigins__0", &urls);

    let (mut rx, child) = sidecar.spawn()?;
    app.state::<Backend>().0.lock().unwrap().replace(child);

    // Wait for readiness on a background task, then navigate the window to the API URL.
    let handle = app.clone();
    tauri::async_runtime::spawn(async move {
        while let Some(event) = rx.recv().await {
            match event {
                CommandEvent::Stdout(bytes) | CommandEvent::Stderr(bytes) => {
                    let line = String::from_utf8_lossy(&bytes);
                    if line.contains("Application started") || line.contains("Now listening on") {
                        if let Some(window) = handle.get_webview_window("main") {
                            if let Ok(url) = url::Url::parse(&format!("http://127.0.0.1:{API_PORT}/")) {
                                let _ = window.navigate(url);
                            }
                        }
                        break;
                    }
                }
                _ => {}
            }
        }
    });

    Ok(())
}

/// The vault key encrypts all provisioned-instance secrets, so it must stay stable across
/// runs. Generate a 32-byte AES-256 key once and persist it in the app-data dir.
fn load_or_create_vault_key(data_dir: &PathBuf) -> Result<String, Box<dyn std::error::Error>> {
    let key_file = data_dir.join("vault.key");
    if let Ok(existing) = std::fs::read_to_string(&key_file) {
        let trimmed = existing.trim().to_string();
        if !trimmed.is_empty() {
            return Ok(trimmed);
        }
    }
    let mut bytes = [0u8; 32];
    rand::RngCore::fill_bytes(&mut rand::thread_rng(), &mut bytes);
    let key = base64::engine::general_purpose::STANDARD.encode(bytes);
    std::fs::write(&key_file, &key)?;
    Ok(key)
}

/// Start the in-process OIDC issuer on its own thread + Tokio runtime so it's independent
/// of Tauri's runtime. It auto-issues tokens (no login screen) for zero-config local sign-in.
fn start_oidc(issuer: String) {
    std::thread::spawn(move || {
        let runtime = match tokio::runtime::Builder::new_current_thread().enable_all().build() {
            Ok(rt) => rt,
            Err(err) => {
                eprintln!("[krint] failed to build OIDC runtime: {err}");
                return;
            }
        };
        runtime.block_on(async move {
            if let Err(err) = oidc::serve(issuer, CLIENT_ID.to_string(), OIDC_PORT).await {
                eprintln!("[krint] OIDC issuer stopped: {err}");
            }
        });
    });
}

// KRINT desktop shell.
//
// The desktop app is a thin Tauri window around the same `KRINT.API` binary the Docker
// image runs. On startup we:
//   1. ensure a stable vault key + SQLite path under the OS app-data dir,
//   2. start a tiny in-process OIDC issuer (zero-config local sign-in, no Docker/Java),
//   3. spawn `KRINT.API` as a sidecar configured for SQLite,
//   4. wait until the API port accepts connections, then point the window at it.
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

// The single OIDC client id. Ports are chosen at runtime (see free_port) so two instances —
// or anything already bound to a fixed port — can't collide.
const CLIENT_ID: &str = "krint";

// Lock the webview down to a native-app feel: no right-click context menu and no devtools
// keyboard shortcuts. Devtools are already off (no `devtools` Cargo feature); this also blocks
// the shortcuts so there's no inspector entry point at all. Re-run on every page load so it
// survives the navigation from the loading screen to the API-served SPA.
const LOCKDOWN_JS: &str = r#"
(function () {
  document.addEventListener('contextmenu', function (e) { e.preventDefault(); }, true);
  document.addEventListener('keydown', function (e) {
    var k = (e.key || '').toUpperCase();
    if (k === 'F12' ||
        (e.ctrlKey && e.shiftKey && (k === 'I' || k === 'J' || k === 'C')) ||
        (e.ctrlKey && k === 'U')) {
      e.preventDefault();
    }
  }, true);
})();
"#;

/// Ask the OS for a free TCP port by binding to :0 and reading back the assignment.
fn free_port() -> std::io::Result<u16> {
    let listener = std::net::TcpListener::bind(("127.0.0.1", 0))?;
    Ok(listener.local_addr()?.port())
}

/// Holds the running API child so we can terminate it on shutdown.
struct Backend(Mutex<Option<CommandChild>>);

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        // Logs go to stdout (dev), the webview console, and a rotating file in the OS log dir
        // so the packaged app is debuggable even without a console (Windows release).
        .plugin(
            tauri_plugin_log::Builder::new()
                .level(log::LevelFilter::Info)
                .target(tauri_plugin_log::Target::new(tauri_plugin_log::TargetKind::Stdout))
                .target(tauri_plugin_log::Target::new(tauri_plugin_log::TargetKind::Webview))
                .target(tauri_plugin_log::Target::new(
                    tauri_plugin_log::TargetKind::LogDir { file_name: Some("krint".into()) },
                ))
                .build(),
        )
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_dialog::init())
        .manage(Backend(Mutex::new(None)))
        .on_page_load(|webview, payload| {
            if payload.event() == tauri::webview::PageLoadEvent::Finished {
                let _ = webview.eval(LOCKDOWN_JS);
            }
        })
        .setup(|app| {
            let handle = app.handle().clone();
            if let Err(err) = start_backend(&handle) {
                log::error!("failed to start backend: {err}");
            }
            // Check for updates in the background; install + restart if one is available.
            let updater_handle = app.handle().clone();
            tauri::async_runtime::spawn(async move {
                check_for_updates(updater_handle).await;
            });
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

/// Check GitHub Releases for a newer signed bundle; if found, download, install, and restart.
/// Updating the bundle replaces the API sidecar + resources too, so one update covers it all.
async fn check_for_updates(app: tauri::AppHandle) {
    use tauri_plugin_updater::UpdaterExt;

    let updater = match app.updater() {
        Ok(u) => u,
        Err(err) => {
            log::warn!("updater unavailable: {err}");
            return;
        }
    };

    match updater.check().await {
        Ok(Some(update)) => {
            use tauri_plugin_dialog::{DialogExt, MessageDialogButtons, MessageDialogKind};

            let accepted = app
                .dialog()
                .message(format!(
                    "KRINT {} is available. Install it now? The app will restart.",
                    update.version
                ))
                .title("Update available")
                .kind(MessageDialogKind::Info)
                .buttons(MessageDialogButtons::OkCancelCustom(
                    "Install & restart".to_string(),
                    "Later".to_string(),
                ))
                .blocking_show();

            if !accepted {
                return;
            }

            log::info!("installing update {}", update.version);
            if let Err(err) = update.download_and_install(|_, _| {}, || {}).await {
                log::error!("update install failed: {err}");
                return;
            }
            app.restart();
        }
        Ok(None) => {}
        Err(err) => log::warn!("update check failed: {err}"),
    }
}

fn start_backend(app: &tauri::AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let data_dir = app.path().app_data_dir()?;
    std::fs::create_dir_all(&data_dir)?;

    let db_path = data_dir.join("krint.db");
    let vault_key = load_or_create_vault_key(&data_dir)?;

    // Pick free ports up front so nothing collides with a port already in use.
    let api_port = free_port()?;
    let oidc_port = free_port()?;

    let authority = format!("http://localhost:{oidc_port}");
    start_oidc(authority.clone(), oidc_port);
    let urls = format!("http://127.0.0.1:{api_port}");
    let conn = format!("Data Source={}", db_path.to_string_lossy());
    let krint_yaml = app
        .path()
        .resolve("resources/krint.yaml", tauri::path::BaseDirectory::Resource)?;
    // The SPA (wwwroot) is bundled under resources/; point the API's content root there so
    // UseStaticFiles + MapFallbackToFile serve it. WebRoot defaults to {ContentRoot}/wwwroot.
    let content_root = app
        .path()
        .resolve("resources", tauri::path::BaseDirectory::Resource)?;

    let sidecar = app
        .shell()
        .sidecar("krint-api")?
        .env("ASPNETCORE_ENVIRONMENT", "Production")
        .env("ASPNETCORE_URLS", &urls)
        .env("ASPNETCORE_CONTENTROOT", content_root.to_string_lossy().to_string())
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
    // Backend -> frontend: tie the sidecar's lifetime to ours so it can't be orphaned. The job
    // object kills it if we die for ANY reason (clean exit, panic, force-kill); the Terminated
    // handler below closes the window if the backend dies first. One never runs without the other.
    confine_child_to_job(child.pid());
    app.state::<Backend>().0.lock().unwrap().replace(child);

    // Drain the sidecar's output continuously so its stdout pipe never fills (which would stall
    // the API) and so logs/early exits are visible for diagnostics.
    let exit_handle = app.clone();
    tauri::async_runtime::spawn(async move {
        while let Some(event) = rx.recv().await {
            match event {
                CommandEvent::Stdout(bytes) | CommandEvent::Stderr(bytes) => {
                    log::info!(target: "krint-api", "{}", String::from_utf8_lossy(&bytes).trim_end());
                }
                CommandEvent::Terminated(payload) => {
                    // The backend is the whole app — if it exits, there's nothing to show, so
                    // bring the window down too instead of leaving a dead shell open.
                    log::warn!("API sidecar exited ({payload:?}); shutting down");
                    exit_handle.exit(payload.code.unwrap_or(1));
                }
                _ => {}
            }
        }
    });

    // Readiness by connection probe, not log-string matching: poll the API port until it
    // accepts a TCP connection, then navigate. Robust against logging config / wording.
    let handle = app.clone();
    std::thread::spawn(move || {
        let addr = std::net::SocketAddr::from(([127, 0, 0, 1], api_port));
        for _ in 0..150 {
            if std::net::TcpStream::connect_timeout(&addr, std::time::Duration::from_millis(500)).is_ok()
            {
                if let Some(window) = handle.get_webview_window("main") {
                    if let Ok(url) = url::Url::parse(&format!("http://127.0.0.1:{api_port}/")) {
                        let _ = window.navigate(url);
                    }
                }
                return;
            }
            std::thread::sleep(std::time::Duration::from_millis(200));
        }
        // ~30s elapsed with no backend: tell the user instead of spinning forever.
        if let Some(window) = handle.get_webview_window("main") {
            let _ = window.eval(
                "document.querySelector('.muted')?.replaceChildren(\
                 document.createTextNode('Backend did not start. Check that Docker is running, then reopen KRINT.'));",
            );
        }
        log::error!("API did not become ready within timeout");
    });

    Ok(())
}

/// Put the sidecar in a Windows Job Object whose handle we hold for the life of the process.
/// `KILL_ON_JOB_CLOSE` means the OS kills the API child the moment this process goes away —
/// including a crash or force-kill, which the graceful `ExitRequested` kill can't cover. The
/// job handle is intentionally never closed: it stays open until we exit, then the OS reaps it.
#[cfg(windows)]
fn confine_child_to_job(pid: u32) {
    use windows_sys::Win32::Foundation::{CloseHandle, FALSE};
    use windows_sys::Win32::System::JobObjects::{
        AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation,
        SetInformationJobObject, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
    };
    use windows_sys::Win32::System::Threading::{OpenProcess, PROCESS_SET_QUOTA, PROCESS_TERMINATE};

    unsafe {
        let job = CreateJobObjectW(std::ptr::null(), std::ptr::null());
        if job.is_null() {
            log::warn!("could not create job object; sidecar may outlive a crash");
            return;
        }
        let mut info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        SetInformationJobObject(
            job,
            JobObjectExtendedLimitInformation,
            &info as *const _ as *const std::ffi::c_void,
            std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
        );
        let process = OpenProcess(PROCESS_SET_QUOTA | PROCESS_TERMINATE, FALSE, pid);
        if process.is_null() {
            log::warn!("could not open sidecar process to confine it");
            CloseHandle(job);
            return;
        }
        if AssignProcessToJobObject(job, process) == 0 {
            log::warn!("could not assign sidecar to job object");
        }
        CloseHandle(process);
        // Deliberately leave `job` open — closing it would kill the child immediately.
    }
}

#[cfg(not(windows))]
fn confine_child_to_job(_pid: u32) {}

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
fn start_oidc(issuer: String, port: u16) {
    std::thread::spawn(move || {
        let runtime = match tokio::runtime::Builder::new_current_thread().enable_all().build() {
            Ok(rt) => rt,
            Err(err) => {
                log::error!("failed to build OIDC runtime: {err}");
                return;
            }
        };
        runtime.block_on(async move {
            if let Err(err) = oidc::serve(issuer, CLIENT_ID.to_string(), port).await {
                log::error!("OIDC issuer stopped: {err}");
            }
        });
    });
}

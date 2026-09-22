// KRINT desktop shell.
//
// The desktop app is a thin Tauri window around the same `KRINT.API` binary the Docker
// image runs. On startup we:
//   1. ensure a stable vault key, a local-login password and the SQLite path under the OS
//      app-data dir,
//   2. spawn `KRINT.API` as a sidecar configured for SQLite and local password login
//      (no identity provider, no Docker/Java for auth),
//   3. wait until the API port accepts connections, then point the window at the login
//      page with the desktop credentials in the URL fragment, which signs it in on its own.
//
// The API serves the SPA itself (Production `MapFallbackToFile`), so the webview talks to a
// single local origin exactly like the Docker deployment.

use std::path::PathBuf;
use std::sync::Mutex;

use base64::Engine;
use tauri::{Manager, RunEvent};
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;

// The one local account the desktop app signs in as. Ports are chosen at runtime (see
// free_port) so two instances — or anything already bound to a fixed port — can't collide.
const DESKTOP_USER: &str = "desktop";

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
            // Tear the API sidecar down when the app exits.
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
    restrict_to_owner(&data_dir)?;

    let db_path = data_dir.join("krint.db");
    let vault_key = load_or_create_vault_key(&data_dir)?;
    let local_password = load_or_create_local_password(&data_dir)?;

    // Pick a free port up front so nothing collides with a port already in use.
    let api_port = free_port()?;

    // Use 127.0.0.1 (not "localhost"): on Windows "localhost" resolves to ::1 first, so every
    // call would eat a failed-IPv6-then-IPv4 fallback delay.
    let urls = format!("http://127.0.0.1:{api_port}");
    let conn = format!("Data Source={}", db_path.to_string_lossy());
    // Where the API binary + its resources (krint.yaml + the SPA's wwwroot) come from: Tauri's
    // bundled sidecar/resources for the installer build, or a self-extracted copy for the single
    // portable exe. WebRoot defaults to {ContentRoot}/wwwroot, so content_root points at resources/.
    let (program, content_root, krint_yaml) = resolve_runtime(app, &data_dir)?;

    let sidecar = program
        .env("ASPNETCORE_ENVIRONMENT", "Production")
        .env("ASPNETCORE_URLS", &urls)
        .env("ASPNETCORE_CONTENTROOT", content_root.to_string_lossy().to_string())
        .env("Database__Provider", "Sqlite")
        .env("ConnectionStrings__KrintDatabase", &conn)
        .env("KRINT_CONFIG", krint_yaml.to_string_lossy().to_string())
        .env("Vault__MasterKey", vault_key)
        // No Oidc__Authority: the API runs local password login and seeds this one account.
        .env("LocalLogin__AdminUserName", DESKTOP_USER)
        .env("LocalLogin__AdminPassword", &local_password)
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
                    // The backend is the whole app, but closing the window the moment it dies
                    // looks like a crash and hides the reason. Say so on the loading screen and
                    // point at the log; the user closes the window when they have read it.
                    log::warn!("API sidecar exited ({payload:?})");
                    let log_dir = exit_handle
                        .path()
                        .app_log_dir()
                        .map(|p| p.to_string_lossy().to_string())
                        .unwrap_or_default();
                    if let Some(window) = exit_handle.get_webview_window("main") {
                        let message = format!(
                            "The local backend stopped (exit code {}). The reason is in the log under {}.",
                            payload.code.unwrap_or(1),
                            log_dir
                        );
                        let _ = window.eval(&format!(
                            "document.querySelector('.muted')?.replaceChildren(document.createTextNode({}));",
                            serde_json::to_string(&message).unwrap_or_default()
                        ));
                    }
                }
                _ => {}
            }
        }
    });

    // Readiness by connection probe, not log-string matching: poll the API port until it
    // accepts a TCP connection, then navigate. Robust against logging config / wording.
    // The credentials ride in the URL fragment: it never reaches the API or its log, and the
    // login page reads it, signs in and clears it before anything else renders.
    let login_url = format!(
        "http://127.0.0.1:{api_port}/login#krint_desktop={}",
        base64::engine::general_purpose::URL_SAFE_NO_PAD
            .encode(format!("{DESKTOP_USER}:{local_password}"))
    );
    let handle = app.clone();
    std::thread::spawn(move || {
        let addr = std::net::SocketAddr::from(([127, 0, 0, 1], api_port));
        for _ in 0..150 {
            if std::net::TcpStream::connect_timeout(&addr, std::time::Duration::from_millis(500)).is_ok()
            {
                if let Some(window) = handle.get_webview_window("main") {
                    if let Ok(url) = url::Url::parse(&login_url) {
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

/// Resolve the API program to spawn plus its content root and krint.yaml path.
///
/// Installer build: Tauri's bundled sidecar + `resources/` (resolved from the bundle).
/// Portable build (`portable` feature): the sidecar + resources are embedded in this exe and
/// self-extracted once into the app-data dir, so a single KRINT.exe runs with no install.
#[cfg(not(feature = "portable"))]
fn resolve_runtime(
    app: &tauri::AppHandle,
    _data_dir: &std::path::Path,
) -> Result<(tauri_plugin_shell::process::Command, PathBuf, PathBuf), Box<dyn std::error::Error>> {
    let content_root = app
        .path()
        .resolve("resources", tauri::path::BaseDirectory::Resource)?;
    let krint_yaml = app
        .path()
        .resolve("resources/krint.yaml", tauri::path::BaseDirectory::Resource)?;
    Ok((app.shell().sidecar("krint-api")?, content_root, krint_yaml))
}

#[cfg(feature = "portable")]
fn resolve_runtime(
    app: &tauri::AppHandle,
    data_dir: &std::path::Path,
) -> Result<(tauri_plugin_shell::process::Command, PathBuf, PathBuf), Box<dyn std::error::Error>> {
    let runtime = extract_payload(data_dir)?;
    let program = app
        .shell()
        .command(runtime.join("krint-api.exe").to_string_lossy().to_string());
    let krint_yaml = runtime.join("krint.yaml");
    Ok((program, runtime, krint_yaml))
}

/// Self-extract the embedded API sidecar + resources into `{app_data}/runtime-{version}/` once.
/// Keyed by version so an upgraded exe re-extracts; reused as-is otherwise for a fast launch.
#[cfg(feature = "portable")]
fn extract_payload(data_dir: &std::path::Path) -> Result<PathBuf, Box<dyn std::error::Error>> {
    static RESOURCES: include_dir::Dir =
        include_dir::include_dir!("$CARGO_MANIFEST_DIR/resources");
    const SIDECAR: &[u8] = include_bytes!(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/binaries/krint-api-x86_64-pc-windows-msvc.exe"
    ));

    let runtime = data_dir.join(concat!("runtime-", env!("CARGO_PKG_VERSION")));
    let sidecar_exe = runtime.join("krint-api.exe");
    // The sidecar is written last, so its presence means a previous extraction completed.
    if !sidecar_exe.exists() {
        std::fs::create_dir_all(&runtime)?;
        RESOURCES.extract(&runtime)?;
        std::fs::write(&sidecar_exe, SIDECAR)?;
    }
    Ok(runtime)
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
    write_private(&key_file, &key)?;
    Ok(key)
}

/// Write a secret so that only the owning account can read it. On Unix that is mode 0600 on
/// the file; `std::fs::write` would leave the default 0644 and hand the vault key to every
/// other login on the machine. On Windows the app-data dir sits under the user's profile,
/// whose ACL already restricts it to that user.
fn write_private(path: &std::path::Path, contents: &str) -> std::io::Result<()> {
    use std::io::Write;
    let mut options = std::fs::OpenOptions::new();
    options.write(true).create(true).truncate(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    let mut file = options.open(path)?;
    file.write_all(contents.as_bytes())
}

/// The app-data dir holds both key files; keep the directory itself owner-only on Unix too.
fn restrict_to_owner(dir: &std::path::Path) -> std::io::Result<()> {
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(dir, std::fs::Permissions::from_mode(0o700))?;
    }
    #[cfg(not(unix))]
    {
        let _ = dir;
    }
    Ok(())
}

/// The password of the desktop account. Like the vault key it is generated once and kept in
/// the app-data dir, so the same account survives restarts and updates. Readable only by the
/// user the app runs as, which is exactly who may use this KRINT.
fn load_or_create_local_password(data_dir: &PathBuf) -> Result<String, Box<dyn std::error::Error>> {
    let file = data_dir.join("local-login.key");
    if let Ok(existing) = std::fs::read_to_string(&file) {
        let trimmed = existing.trim().to_string();
        if !trimmed.is_empty() {
            return Ok(trimmed);
        }
    }
    let mut bytes = [0u8; 24];
    rand::RngCore::fill_bytes(&mut rand::thread_rng(), &mut bytes);
    let password = base64::engine::general_purpose::URL_SAFE_NO_PAD.encode(bytes);
    write_private(&file, &password)?;
    Ok(password)
}

namespace KRINT.API
{
    /// <summary>
    /// The desktop shell (src-tauri) runs this API as a sidecar and sets Desktop__Enabled=true.
    /// It signs in on every launch with a password it keeps in its own app-data dir, so anything
    /// that changes that account's password from inside KRINT locks the desktop user out.
    /// </summary>
    public static class DesktopShell
    {
        public static bool IsEnabled(IConfiguration configuration) =>
            configuration.GetValue<bool>("Desktop:Enabled");
    }
}

namespace KRINT.Infrastructure.Services
{
    /// <summary>Shell rewrites applied to [sh|bash, -c, script, args...] exec commands.</summary>
    internal static class ExecScripts
    {
        public static bool IsShellCommand(IList<string> command)
            => command.Count >= 3 && command[1] == "-c" && (command[0] == "bash" || command[0] == "sh");

        /// <summary>Feeds <paramref name="path"/> to the whole script as stdin. Appending
        /// "&lt; file" after the script would only redirect its last command, so the script is
        /// wrapped in a brace group (same shell, so exit/trap/set behave as before). The newline
        /// before the closing brace keeps a trailing comment or "&amp;" from swallowing it.</summary>
        public static string RedirectStdin(string script, string path)
            => "{ " + script + "\n} < " + Quote(path);

        /// <summary>Records the shell's pid in <paramref name="pidFile"/> before running the script,
        /// so a timed-out exec can be killed from a second exec. A read-only /tmp only loses that.</summary>
        public static string WithPidFile(string script, string pidFile)
            => "{ echo $$ > " + Quote(pidFile) + "; } 2>/dev/null\n" + script;

        /// <summary>Sends SIGTERM to the pid in $1's file and every descendant. The tree is collected
        /// first because killing a parent reparents its children to init.</summary>
        public const string KillTreeScript =
            "f=\"$1\"; [ -f \"$f\" ] || exit 0; t=\"$(cat \"$f\")\"; l=\"$t\"; " +
            "while [ -n \"$t\" ]; do n=\"\"; for p in $t; do for s in /proc/[0-9]*/stat; do " +
            "read -r a b c d e < \"$s\" 2>/dev/null || continue; [ \"$d\" = \"$p\" ] && n=\"$n $a\"; " +
            "done; done; l=\"$l $n\"; t=\"$n\"; done; kill -TERM $l 2>/dev/null; rm -f \"$f\"; exit 0";

        private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
    }
}

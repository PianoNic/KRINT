namespace KRINT.Tests.Services
{
    /// <summary>
    /// The desktop app bundles src-tauri/resources/krint.yaml. scripts/publish-sidecar.mjs copies
    /// the root krint.yaml over it on every desktop build; this catches a commit that edits one
    /// without the other (an engine added to the root but missing its desktop port range).
    /// </summary>
    public class DesktopKrintYamlTests
    {
        [Test]
        public async Task DesktopKrintYaml_MatchesRootKrintYaml()
        {
            var root = FindRepoRoot();
            var rootYaml = Normalize(await File.ReadAllTextAsync(Path.Combine(root, "krint.yaml")));
            var desktopYaml = Normalize(await File.ReadAllTextAsync(Path.Combine(root, "src-tauri", "resources", "krint.yaml")));

            await Assert.That(desktopYaml).IsEqualTo(rootYaml);
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

        private static string FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "KRINT.slnx"))) return dir.FullName;
            }
            throw new InvalidOperationException("Could not find the repository root (KRINT.slnx) above " + AppContext.BaseDirectory);
        }
    }
}

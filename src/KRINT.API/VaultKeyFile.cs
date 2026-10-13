using System.Security.Cryptography;

namespace KRINT.API
{
    /// <summary>
    /// Gives a deployment a vault master key without anyone having to make one up. When
    /// <c>Vault:MasterKey</c> is set it is used as is. Otherwise the key is read from
    /// <c>Vault:KeyFile</c> (default <c>data/vault.key</c> under the content root, which the
    /// compose stack mounts), and generated there on first boot. This replaces the fixed key the
    /// clone-and-run stack used to ship, which every clone shared and from which the local-login
    /// token signing key is derived.
    /// </summary>
    public static class VaultKeyFile
    {
        /// <summary>The key compose.yml shipped before keys were generated. Public, so anyone can
        /// decrypt data written with it and mint local-login tokens from it.</summary>
        public const string RetiredDemoKey = "2P+vvGUw5v3weZdFU04YOicpVNs9eIyRKo6NliiDR4c=";

        /// <summary>Where the key came from this boot, for the startup log. Null when configured directly.</summary>
        public static string? KeyFilePath { get; private set; }

        /// <summary>True when this boot generated a new key file.</summary>
        public static bool Generated { get; private set; }

        public static bool IsRetiredDemoKey(string? key) => string.Equals(key?.Trim(), RetiredDemoKey, StringComparison.Ordinal);

        public static void Apply(WebApplicationBuilder builder)
        {
            if (!string.IsNullOrWhiteSpace(builder.Configuration["Vault:MasterKey"])) return;

            var configured = builder.Configuration["Vault:KeyFile"];
            var path = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
                ? Path.Combine("data", "vault.key")
                : configured, builder.Environment.ContentRootPath);

            string key;
            try
            {
                if (File.Exists(path))
                {
                    key = File.ReadAllText(path).Trim();
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                    // CreateNew: never overwrite a key another process wrote in the meantime.
                    var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                    if (!OperatingSystem.IsWindows())
                        options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    using (var writer = new StreamWriter(path, options))
                        writer.Write(key);
                    Generated = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"Vault__MasterKey is not set and the key file '{path}' could not be read or created ({ex.Message}). " +
                    "Set Vault__MasterKey, or mount a writable directory there (compose.yml mounts ./data).", ex);
            }

            KeyFilePath = path;
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Vault:MasterKey"] = key });
        }
    }
}

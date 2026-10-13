using KRINT.Application.Queries.SupportedDatabase;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KRINT.Application.Options
{
    /// <summary>Loads instances.yaml on demand. Returns an empty list when the path is unset
    /// or the file doesn't exist so the reconcile hosted service can no-op in that case.</summary>
    public interface IInstancesConfigLoader
    {
        InstancesConfig Load();
        /// <summary>Absolute path to the file we're going to read, or null when none is configured.</summary>
        string? ResolvedPath { get; }
    }

    public sealed class InstancesConfigLoader(string configDir, string? relativeOrAbsolutePath) : IInstancesConfigLoader
    {
        public string? ResolvedPath { get; } = string.IsNullOrWhiteSpace(relativeOrAbsolutePath)
            ? null
            : Path.IsPathRooted(relativeOrAbsolutePath)
                ? relativeOrAbsolutePath
                : Path.GetFullPath(Path.Combine(configDir, relativeOrAbsolutePath));

        public InstancesConfig Load()
        {
            if (ResolvedPath is null || !File.Exists(ResolvedPath))
                return new InstancesConfig();

            // Same convention as krint.yaml so users don't have to flip naming styles mid-file.
            // Strict on purpose: a misspelt key ("displayname", "grant_database") used to be dropped
            // silently and the entry provisioned without it. Failing the parse names the key.
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            using var reader = File.OpenText(ResolvedPath);
            var config = deserializer.Deserialize<InstancesConfig>(reader) ?? new InstancesConfig();
            ValidatePlugins(config);
            return config;
        }

        /// <summary>
        /// Same reasoning as the strict parse: an unknown plugin key would otherwise be skipped at
        /// provision time and the instance created without the extension the file asked for.
        /// </summary>
        private static void ValidatePlugins(InstancesConfig config)
        {
            foreach (var instance in config.Instances)
            {
                if (instance.Plugins.Count == 0) continue;
                var allowed = GetSupportedDatabasesQueryHandler.PluginKeysFor(instance.Engine);
                var unknown = instance.Plugins
                    .Where(p => !allowed.Contains(p, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                if (unknown.Count == 0) continue;

                var hint = allowed.Count == 0
                    ? $"engine '{instance.Engine}' has no plugins"
                    : $"valid keys for '{instance.Engine}': {string.Join(", ", allowed)}";
                throw new InvalidDataException(
                    $"Instance '{instance.DisplayName}' lists unknown plugin(s) {string.Join(", ", unknown.Select(u => $"'{u}'"))} ({hint}).");
            }
        }
    }
}

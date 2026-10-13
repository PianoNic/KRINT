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
            return Normalize(deserializer.Deserialize<InstancesConfig>(reader) ?? new InstancesConfig());
        }

        /// <summary>A key with no value (e.g. <c>instances:</c> with every entry commented out)
        /// deserializes to null and overrides the property initializer. Replace those with empty
        /// lists so callers never see null.</summary>
        internal static InstancesConfig Normalize(InstancesConfig config)
        {
            config.Instances = (config.Instances ?? []).Where(i => i is not null).ToList();
            foreach (var spec in config.Instances)
            {
                spec.Engine ??= "";
                spec.Version ??= "";
                spec.DisplayName ??= "";
                spec.Databases =(spec.Databases ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
                spec.Plugins = (spec.Plugins ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                spec.Users = (spec.Users ?? []).Where(u => u is not null).ToList();
                foreach (var user in spec.Users)
                {
                    user.Name ??= "";
                    user.GrantDatabases = (user.GrantDatabases ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
                }
            }
            return config;
        }
    }
}

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
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            using var reader = File.OpenText(ResolvedPath);
            return deserializer.Deserialize<InstancesConfig>(reader) ?? new InstancesConfig();
        }
    }
}

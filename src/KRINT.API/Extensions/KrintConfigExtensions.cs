using KRINT.Application.Options;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KRINT.API.Extensions;

public static class KrintConfigExtensions
{
    /// <summary>Where krint.yaml was read from, or null when the process runs on defaults. Set
    /// during registration so the startup banner can name it.</summary>
    public static string? ResolvedConfigPath { get; private set; }

    private const string FileName = "krint.yaml";
    private const string EnvVar = "KRINT_CONFIG";

    public static IServiceCollection AddKrintConfig(this IServiceCollection services, IHostEnvironment env)
    {
        var path = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            path = FindUpwards(env.ContentRootPath);
        ResolvedConfigPath = path is not null && File.Exists(path) ? Path.GetFullPath(path) : null;

        if (path is null || !File.Exists(path))
        {
            // No config file present: start with defaults rather than crashing. Provisioning
            // needs port ranges (krint.yaml -> krint.port_ranges), so those operations surface a
            // clear error until configured, but the app boots — important for the desktop build.
            Console.Error.WriteLine($"[KRINT] No {FileName} found (set {EnvVar} or place {FileName} at the content root). Starting with defaults.");
            var defaults = new KrintOptions();
            services.AddSingleton<IOptions<KrintOptions>>(Options.Create(defaults));
            services.AddSingleton<IInstancesConfigLoader>(_ => new InstancesConfigLoader(env.ContentRootPath, defaults.InstancesFile));
            return services;
        }

        KrintOptions options;
        using (var reader = File.OpenText(path))
            options = ParseOptions(reader);
        services.AddSingleton<IOptions<KrintOptions>>(Options.Create(options));

        // Remember where krint.yaml lives so InstancesFile (when relative) resolves against it.
        var configDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? env.ContentRootPath;
        services.AddSingleton<IInstancesConfigLoader>(_ => new InstancesConfigLoader(configDir, options.InstancesFile));
        return services;
    }

    /// <summary>Parses the krint.yaml body. A key with no value (e.g. <c>nodes:</c> with every
    /// entry commented out) deserializes to null and overrides the property initializer, so those
    /// are replaced with empty collections here and nothing downstream has to null-check.</summary>
    public static KrintOptions ParseOptions(TextReader reader)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var root = deserializer.Deserialize<KrintConfigFile>(reader)
            ?? throw new InvalidOperationException($"{FileName} is empty or invalid.");

        var options = root.Krint ?? new KrintOptions();
        options.PortRanges = (options.PortRanges ?? [])
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        options.Storage ??= new();
        options.Nodes = (options.Nodes ?? []).Where(n => n is not null).ToList();
        return options;
    }

    private static string? FindUpwards(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private sealed class KrintConfigFile
    {
        public KrintOptions? Krint { get; set; }
    }
}

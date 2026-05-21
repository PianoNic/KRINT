using KRINT.Application.Options;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KRINT.API.Extensions;

public static class KrintConfigExtensions
{
    private const string FileName = "krint.yaml";
    private const string EnvVar = "KRINT_CONFIG";

    public static IServiceCollection AddKrintConfig(this IServiceCollection services, IHostEnvironment env)
    {
        var path = Environment.GetEnvironmentVariable(EnvVar)
            ?? FindUpwards(env.ContentRootPath)
            ?? throw new InvalidOperationException($"Could not find {FileName}. Set {EnvVar} or place {FileName} at the repo root.");

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        using var reader = File.OpenText(path);
        var root = deserializer.Deserialize<KrintConfigFile>(reader)
            ?? throw new InvalidOperationException($"{FileName} is empty or invalid.");

        var options = root.Krint ?? new KrintOptions();
        services.AddSingleton<IOptions<KrintOptions>>(Options.Create(options));
        return services;
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

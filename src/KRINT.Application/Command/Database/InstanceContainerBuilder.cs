using System.Text.Json;
using Docker.DotNet.Models;
using KRINT.Application.Dtos.SupportedDatabase;
using KRINT.Application.Queries.SupportedDatabase;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Database
{
    /// <summary>Everything that decides how a KRINT-managed container is built. Create, visibility
    /// change and upgrade all go through <see cref="InstanceContainerBuilder.Build"/> so a recreated
    /// container can't drift from the one that was provisioned (image swap, plugin env, storage).</summary>
    public sealed record InstanceContainerSpec(
        string Engine,
        string Version,
        Guid InstanceId,
        string DisplayName,
        string ContainerName,
        string Password,
        string DatabaseName,
        IReadOnlyList<EnginePluginDto> Plugins,
        int HostPort,
        bool LoopbackOnly,
        string StorageBind,
        string? ExtraBindIp = null);

    public static class InstanceContainerBuilder
    {
        public static CreateContainerParameters Build(InstanceContainerSpec s)
        {
            var spec = CreateDatabaseCommandHandler.ResolveEngineSpec(s.Engine, s.Version);
            var (imageName, imageTag) = ResolveImage(s.Engine, s.Version, s.Plugins);

            var env = CreateDatabaseCommandHandler.BuildEnv(s.Engine, s.Password, s.DatabaseName, spec.DefaultDatabase);
            env = MergeEnv(env, s.Plugins.Where(p => p.InstallMode == PluginInstallMode.EnvFlag).Select(p => p.Payload));

            return new CreateContainerParameters
            {
                Image = $"{imageName}:{imageTag}",
                Name = s.ContainerName,
                Env = env,
                Cmd = spec.CmdFactory?.Invoke(s.Password),
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    [$"{spec.InternalPort}/tcp"] = default,
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        [$"{spec.InternalPort}/tcp"] = HostBindings(s),
                    },
                    Binds = new List<string> { s.StorageBind },
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                },
                Labels = Containers.KrintContainerLabels.For(s.Engine, s.InstanceId, s.DisplayName),
            };
        }

        /// <summary>Leaving HostIP unset is Docker's default and binds to 0.0.0.0 (visible on the
        /// LAN). A private instance binds 127.0.0.1, plus the host-internal bridge gateway when
        /// <see cref="InstanceContainerSpec.ExtraBindIp"/> is set, so other containers on the same
        /// host reach it while nothing outside the host can.</summary>
        public static IList<PortBinding> HostBindings(InstanceContainerSpec s)
        {
            var port = s.HostPort.ToString();
            if (!s.LoopbackOnly) return new List<PortBinding> { new() { HostPort = port, HostIP = "" } };
            var bindings = new List<PortBinding> { new() { HostPort = port, HostIP = "127.0.0.1" } };
            if (!string.IsNullOrEmpty(s.ExtraBindIp))
                bindings.Add(new PortBinding { HostPort = port, HostIP = s.ExtraBindIp });
            return bindings;
        }

        /// <summary>The image (name, tag) an instance runs. Image-swap plugins publish their own tag
        /// schemes, so the engine version is translated per image instead of reused verbatim.</summary>
        public static (string Name, string Tag) ResolveImage(string engine, string version, IReadOnlyList<EnginePluginDto> plugins)
        {
            var spec = CreateDatabaseCommandHandler.ResolveEngineSpec(engine, version);
            var name = plugins.FirstOrDefault(p => p.InstallMode == PluginInstallMode.DockerImageSwap)?.Payload ?? spec.Image;
            var tag = name switch
            {
                "pgvector/pgvector" => CreateDatabaseCommandHandler.PgVectorTagFor(version),
                "postgis/postgis" => PostgisTagFor(version),
                "redis/redis-stack-server" => RedisStackTagFor(version),
                _ => version,
            };
            return (name, tag);
        }

        /// <summary>postgis/postgis tags are &lt;pg major&gt;-&lt;postgis minor&gt;: 12-17 ship 3.5,
        /// 18 and later ship 3.6. Anything without a readable major falls back to latest.</summary>
        internal static string PostgisTagFor(string postgresVersion)
        {
            var major = CreateDatabaseCommandHandler.TryGetPostgresMajor(postgresVersion);
            return major switch
            {
                null => "latest",
                >= 18 => $"{major}-3.6",
                _ => $"{major}-3.5",
            };
        }

        /// <summary>redis/redis-stack-server tags are &lt;redis&gt;-v&lt;n&gt; and only exist for the
        /// 6.2, 7.2 and 7.4 lines. Redis 8+ already bundles the stack modules upstream and has no
        /// redis-stack-server build, so it (and anything unrecognized) uses latest.</summary>
        internal static string RedisStackTagFor(string redisVersion)
        {
            var parts = redisVersion.Split('.', '-');
            var majorMinor = parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : parts[0];
            return majorMinor switch
            {
                "6.2" => "6.2.6-v20",
                "7.2" => "7.2.0-v20",
                "7.4" => "7.4.0-v8",
                _ => "latest",
            };
        }

        /// <summary>Tags that move over time and so must be re-pulled even when present locally.</summary>
        public static bool IsRollingTag(string tag) =>
            tag.Contains("latest", StringComparison.OrdinalIgnoreCase)
            || tag.Contains("edge", StringComparison.OrdinalIgnoreCase)
            || tag.Contains("master", StringComparison.OrdinalIgnoreCase)
            || tag.Contains("nightly", StringComparison.OrdinalIgnoreCase);

        /// <summary>Pulls image:tag unless it is already on the daemon (rolling tags are always pulled).</summary>
        public static async Task EnsureImageAsync(IDockerService docker, string image, string tag, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            if (!IsRollingTag(tag) && await docker.ImageExistsAsync(image, tag, cancellationToken))
                return;
            progress?.Report($"Pulling image {image}:{tag} (first time can take a while)");
            await docker.PullImageAsync(image, tag, cancellationToken);
        }

        /// <summary>Resolves plugin keys against the catalog. Unknown keys are dropped (the UI only
        /// offers catalog plugins); more than one image-swap plugin is rejected because only one
        /// image can win.</summary>
        public static IReadOnlyList<EnginePluginDto> ResolvePlugins(IEnumerable<string>? keys)
        {
            if (keys is null) return Array.Empty<EnginePluginDto>();
            var catalog = GetSupportedDatabasesQueryHandler.AllPluginsByKey;
            var result = new List<EnginePluginDto>();
            foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (catalog.TryGetValue(key.Trim(), out var plugin)) result.Add(plugin);
            }

            var swaps = result.Where(p => p.InstallMode == PluginInstallMode.DockerImageSwap).ToList();
            if (swaps.Count > 1)
                throw new ArgumentException($"Plugins {string.Join(" and ", swaps.Select(p => p.DisplayName))} each replace the container image and can't be combined. Pick one.");
            return result;
        }

        /// <summary>Rejects plugins the engine doesn't offer (e.g. PostGIS on TimescaleDB).</summary>
        public static void RequireSupportedByEngine(string engine, IReadOnlyList<EnginePluginDto> plugins)
        {
            var offered = GetSupportedDatabasesQueryHandler.PluginsFor(engine);
            var unsupported = plugins.Where(p => !offered.Any(o => string.Equals(o.Key, p.Key, StringComparison.OrdinalIgnoreCase))).ToList();
            if (unsupported.Count > 0)
                throw new ArgumentException($"Plugin(s) {string.Join(", ", unsupported.Select(p => p.DisplayName))} are not available for {engine}.");
        }

        public static string? SerializePluginKeys(IReadOnlyList<EnginePluginDto> plugins) =>
            plugins.Count == 0 ? null : string.Join(',', plugins.Select(p => p.Key));

        public static IReadOnlyList<string> ParsePluginKeys(string? stored) =>
            string.IsNullOrWhiteSpace(stored) ? Array.Empty<string>() : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>The plugins of an instance: the persisted keys, or for rows that predate the
        /// column, whatever the running container shows (its image and env flags).</summary>
        public static IReadOnlyList<EnginePluginDto> PluginsForInstance(KRINT.Domain.DatabaseInstance instance, ContainerInspectResponse? inspect) =>
            instance.Plugins is not null || inspect is null
                ? ResolvePlugins(ParsePluginKeys(instance.Plugins))
                : ResolvePlugins(InferPluginKeys(instance.Engine, instance.Version, inspect));

        internal static IReadOnlyList<string> InferPluginKeys(string engine, string version, ContainerInspectResponse inspect)
        {
            var keys = new List<string>();
            var offered = GetSupportedDatabasesQueryHandler.PluginsFor(engine);
            var stockImage = CreateDatabaseCommandHandler.ResolveEngineSpec(engine, version).Image;
            var runningImage = StripTag(inspect.Config?.Image ?? "");
            var env = inspect.Config?.Env ?? new List<string>();

            foreach (var plugin in offered)
            {
                switch (plugin.InstallMode)
                {
                    case PluginInstallMode.DockerImageSwap:
                        if (runningImage != stockImage && string.Equals(runningImage, plugin.Payload, StringComparison.OrdinalIgnoreCase))
                            keys.Add(plugin.Key);
                        break;
                    case PluginInstallMode.EnvFlag:
                        if (EnvFlagPresent(env, plugin.Payload))
                            keys.Add(plugin.Key);
                        break;
                    // PgExtension plugins live in the data itself and need no reapplying.
                }
            }
            return keys;
        }

        /// <summary>The bind spec of the data mount on an existing container, or null when the
        /// container has none at <paramref name="dataPath"/>.</summary>
        public static string? ReadStorageBind(ContainerInspectResponse inspect, string dataPath)
        {
            var bind = inspect.HostConfig?.Binds?.FirstOrDefault(b =>
                TrySplitBind(b, out _, out var target) && string.Equals(target, dataPath, StringComparison.Ordinal));
            if (bind is not null && TrySplitBind(bind, out var src, out var dst)) return $"{src}:{dst}";

            var mount = inspect.Mounts?.FirstOrDefault(m => string.Equals(m.Destination, dataPath, StringComparison.Ordinal));
            if (mount is null) return null;
            var source = string.Equals(mount.Type, "volume", StringComparison.OrdinalIgnoreCase) ? mount.Name : mount.Source;
            return string.IsNullOrEmpty(source) ? null : $"{source}:{dataPath}";
        }

        /// <summary>Splits "source:/container/path[:mode]" into its source and container path. The
        /// source may itself contain a colon (a Windows drive letter), so split on the last ":/".</summary>
        public static bool TrySplitBind(string bind, out string source, out string target)
        {
            source = target = "";
            var idx = bind.LastIndexOf(":/", StringComparison.Ordinal);
            if (idx <= 0) return false;
            source = bind[..idx];
            target = bind[(idx + 1)..];
            var mode = target.IndexOf(':');
            if (mode >= 0) target = target[..mode];
            return true;
        }

        /// <summary>True when a bind source is a named volume rather than a host path.</summary>
        public static bool IsVolumeSource(string source) =>
            !source.Contains('/') && !source.Contains('\\');

        /// <summary>User-defined networks a container is attached to, excluding Docker's defaults.
        /// Reattached when the container is recreated.</summary>
        public static IReadOnlyList<string> UserNetworks(ContainerInspectResponse? inspect) =>
            inspect?.NetworkSettings?.Networks?.Keys
                .Where(n => n is not ("bridge" or "host" or "none"))
                .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

        public static async Task ReattachNetworksAsync(IDockerService docker, string containerId, IReadOnlyList<string> networks, CancellationToken cancellationToken)
        {
            foreach (var network in networks)
            {
                // KRINT's own networks are already joined on create; a gone network is not fatal.
                try { await docker.ConnectNetworkAsync(network, containerId, cancellationToken); }
                catch when (!cancellationToken.IsCancellationRequested) { }
            }
        }

        private static string StripTag(string image)
        {
            var at = image.IndexOf('@');
            if (at >= 0) image = image[..at];
            var colon = image.LastIndexOf(':');
            return colon > image.LastIndexOf('/') ? image[..colon] : image;
        }

        // Env flag payloads look like KEY=["a"]. Several flags with the same key (e.g. APOC and GDS
        // both set NEO4J_PLUGINS) would overwrite each other, so JSON-array values are merged.
        internal static List<string> MergeEnv(List<string> env, IEnumerable<string> flags)
        {
            var result = new List<string>(env);
            foreach (var flag in flags)
            {
                var (key, value) = SplitEnv(flag);
                var existingIndex = result.FindIndex(e => SplitEnv(e).Key == key);
                if (existingIndex >= 0 && TryParseArray(SplitEnv(result[existingIndex]).Value, out var existing) && TryParseArray(value, out var added))
                {
                    var merged = existing.Concat(added).Distinct(StringComparer.Ordinal).ToList();
                    result[existingIndex] = $"{key}={JsonSerializer.Serialize(merged)}";
                }
                else if (existingIndex >= 0)
                {
                    result[existingIndex] = flag;
                }
                else
                {
                    result.Add(flag);
                }
            }
            return result;
        }

        private static bool EnvFlagPresent(IList<string> env, string flag)
        {
            var (key, value) = SplitEnv(flag);
            foreach (var entry in env)
            {
                var (k, v) = SplitEnv(entry);
                if (k != key) continue;
                if (TryParseArray(value, out var wanted) && TryParseArray(v, out var actual))
                    return wanted.All(actual.Contains);
                return v == value;
            }
            return false;
        }

        private static (string Key, string Value) SplitEnv(string entry)
        {
            var eq = entry.IndexOf('=');
            return eq < 0 ? (entry, "") : (entry[..eq], entry[(eq + 1)..]);
        }

        private static bool TryParseArray(string value, out List<string> items)
        {
            items = new List<string>();
            if (!value.TrimStart().StartsWith('[')) return false;
            try
            {
                items = JsonSerializer.Deserialize<List<string>>(value) ?? new List<string>();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}

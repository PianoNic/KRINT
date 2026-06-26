using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KRINT.Infrastructure.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// Resolves the list of provisionable versions for an engine straight from the registry that
    /// publishes its image (Docker Hub or Microsoft Container Registry). This keeps the create
    /// screen current automatically instead of relying on a hand-maintained list that drifts.
    ///
    /// Per engine we know the image repository and a regex selecting the "clean" version tags
    /// (dropping OS variants, betas, and rolling aliases). Results are sorted newest-first, capped,
    /// and cached for 24h. If the registry can't be reached we fall back to a last-known-good list
    /// so the screen keeps working offline.
    /// </summary>
    public partial class DatabaseVersionService(HttpClient http, IMemoryCache cache, ILogger<DatabaseVersionService> logger) : IDatabaseVersionService
    {
        private enum Registry { DockerHub, Mcr }

        // LineDepth = how many leading numeric components define a release line for this engine
        // (Postgres lines are majors -> 1; MySQL/Mongo/CouchDB lines are major.minor -> 2). For each
        // of the newest MaxGroups lines we surface the latest specific tag plus the bare line tag,
        // which reproduces the clean "18.4 · 18 · 17.10 · 17" shape from live registry data.
        private record VersionSource(Registry Registry, string Repository, Regex TagPattern, int LineDepth = 2, int MaxGroups = 6);

        // engineKey -> where its image lives + which tags count as versions. Verified against the
        // live registries; the regexes intentionally exclude variant tags (e.g. "18-bookworm",
        // "19beta1", sha digests) so the dropdown shows only real versions.
        private static readonly IReadOnlyDictionary<string, VersionSource> Sources = new Dictionary<string, VersionSource>
        {
            ["postgres"]    = new(Registry.DockerHub, "library/postgres",              Rx(@"^\d+(\.\d+)?$"),       LineDepth: 1, MaxGroups: 6),
            ["mysql"]       = new(Registry.DockerHub, "library/mysql",                 Rx(@"^\d+(\.\d+){0,2}$"),   LineDepth: 1, MaxGroups: 3),
            ["mariadb"]     = new(Registry.DockerHub, "library/mariadb",               Rx(@"^\d+(\.\d+){0,2}$"),   LineDepth: 1, MaxGroups: 3),
            ["mongo"]       = new(Registry.DockerHub, "library/mongo",                 Rx(@"^\d+(\.\d+){0,2}$"),   LineDepth: 1, MaxGroups: 4),
            ["redis"]       = new(Registry.DockerHub, "library/redis",                 Rx(@"^\d+(\.\d+){0,2}$"),   LineDepth: 1, MaxGroups: 4),
            ["cassandra"]   = new(Registry.DockerHub, "library/cassandra",             Rx(@"^\d+(\.\d+){0,2}$"),   LineDepth: 1, MaxGroups: 3),
            ["valkey"]      = new(Registry.DockerHub, "valkey/valkey",                 Rx(@"^\d+(\.\d+){0,2}$"),   LineDepth: 1, MaxGroups: 3),
            ["neo4j"]       = new(Registry.DockerHub, "library/neo4j",                 Rx(@"^\d+(\.\d+){0,2}$"),   LineDepth: 1, MaxGroups: 3),
            ["couchdb"]     = new(Registry.DockerHub, "library/couchdb",               Rx(@"^\d+(\.\d+){0,2}$"),   LineDepth: 2, MaxGroups: 4),
            ["cockroachdb"] = new(Registry.DockerHub, "cockroachdb/cockroach",         Rx(@"^v\d+\.\d+(\.\d+)?$"), LineDepth: 2, MaxGroups: 6),
            ["qdrant"]      = new(Registry.DockerHub, "qdrant/qdrant",                 Rx(@"^v\d+(\.\d+){0,2}$"),  LineDepth: 2, MaxGroups: 5),
            ["timescaledb"] = new(Registry.DockerHub, "timescale/timescaledb",         Rx(@"^latest-pg\d+$"),      LineDepth: 1, MaxGroups: 6),
            ["clickhouse"]  = new(Registry.DockerHub, "clickhouse/clickhouse-server",  Rx(@"^\d+\.\d+$"),          LineDepth: 2, MaxGroups: 8),
            ["pgvector"]    = new(Registry.DockerHub, "pgvector/pgvector",             Rx(@"^pg\d+$"),             LineDepth: 1, MaxGroups: 8),
            ["seaweedfs"]   = new(Registry.DockerHub, "chrislusf/seaweedfs",           Rx(@"^\d+\.\d+(\.\d+)?$"),  LineDepth: 2, MaxGroups: 6),
            ["mssql"]       = new(Registry.Mcr,       "mssql/server",                  Rx(@"^\d{4}-latest$"),      LineDepth: 1, MaxGroups: 4),
            ["azurite"]     = new(Registry.Mcr,       "azure-storage/azurite",         Rx(@"^\d+\.\d+\.\d+$"),     LineDepth: 2, MaxGroups: 5),
        };

        // Last-known-good lists, used only when the registry lookup fails (offline / rate-limited).
        // "latest" exists for every image, so provisioning still works even without a curated entry.
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Fallback = new Dictionary<string, IReadOnlyList<string>>
        {
            ["postgres"]    = new[] { "18", "17", "16", "15", "14" },
            ["mysql"]       = new[] { "9", "8.4", "8.0" },
            ["mariadb"]     = new[] { "11.4", "10.11", "10.6" },
            ["mongo"]       = new[] { "8.0", "7.0" },
            ["redis"]       = new[] { "8", "7.4", "7.2" },
            ["cassandra"]   = new[] { "5.0", "4.1", "4.0" },
            ["neo4j"]       = new[] { "5.26", "5" },
            ["couchdb"]     = new[] { "3.5", "3.4", "3.3" },
            ["cockroachdb"] = new[] { "v26.2.0", "v25.4.10", "v24.1.29" },
            ["timescaledb"] = new[] { "latest-pg18", "latest-pg17", "latest-pg16" },
            ["clickhouse"]  = new[] { "26.5", "26.4", "26.3" },
            ["pgvector"]    = new[] { "pg18", "pg17", "pg16" },
            ["qdrant"]      = new[] { "v1.18", "v1" },
            ["valkey"]      = new[] { "9.1", "8.1" },
            ["seaweedfs"]   = new[] { "4.33", "4.32" },
            ["mssql"]       = new[] { "2025-latest", "2022-latest", "2019-latest" },
            ["azurite"]     = new[] { "latest" },
        };

        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
        private const int DockerHubPages = 3; // 300 most-recent tags; enough to cover every live major.

        public async Task<IReadOnlyList<string>> GetSupportedVersionsAsync(string engineKey, CancellationToken cancellationToken = default)
        {
            if (!Sources.TryGetValue(engineKey, out var source))
            {
                throw new ArgumentException($"Unknown engine key '{engineKey}'.", nameof(engineKey));
            }

            var cacheKey = $"versions:{engineKey}";
            if (cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            {
                return cached;
            }

            IReadOnlyList<string> versions;
            try
            {
                var tags = source.Registry == Registry.DockerHub
                    ? await FetchDockerHubTagsAsync(source.Repository, cancellationToken)
                    : await FetchMcrTagsAsync(source.Repository, cancellationToken);

                versions = SelectVersions(tags, source);

                if (versions.Count == 0)
                {
                    logger.LogWarning("No matching version tags for '{Engine}' ({Repo}); using fallback.", engineKey, source.Repository);
                    return Fallback.TryGetValue(engineKey, out var fb) ? fb : new[] { "latest" };
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The registry is an optional convenience lookup, not a hard dependency. On failure
                // return the last-known-good list (not cached, so it retries next time).
                logger.LogWarning(ex, "Version lookup for '{Engine}' failed; using fallback.", engineKey);
                return Fallback.TryGetValue(engineKey, out var fb) ? fb : new[] { "latest" };
            }

            cache.Set(cacheKey, versions, CacheDuration);
            return versions;
        }

        // Keep the newest specific tag + the bare line tag for each of the newest MaxGroups release
        // lines. e.g. Postgres (depth 1): line 18 -> "18.4" (newest) + "18" (bare); line 17 -> "17.10" + "17".
        private static IReadOnlyList<string> SelectVersions(IReadOnlyList<string> tags, VersionSource source)
        {
            var groups = tags
                .Where(t => source.TagPattern.IsMatch(t))
                .Distinct()
                .Select(t => (tag: t, key: NumericKey(t)))
                .Where(x => x.key.Length > 0)
                .GroupBy(x => x.key.Take(source.LineDepth).ToArray(), IntSequenceEqualityComparer.Instance)
                .Select(g => g.OrderByDescending(x => x.key, IntSequenceComparer.Instance)
                              .ThenByDescending(x => x.tag, StringComparer.Ordinal)
                              .Select(x => x.tag)
                              .ToList())
                .OrderByDescending(line => NumericKey(line[0]), IntSequenceComparer.Instance)
                .Take(source.MaxGroups);

            var versions = new List<string>();
            foreach (var line in groups)
            {
                versions.Add(line[0]);            // newest specific tag in the line
                var bare = line[^1];              // shortest tag in the line = the bare rolling tag
                if (!versions.Contains(bare)) versions.Add(bare);
            }
            return versions;
        }

        private async Task<IReadOnlyList<string>> FetchDockerHubTagsAsync(string repository, CancellationToken ct)
        {
            var names = new List<string>();
            for (var page = 1; page <= DockerHubPages; page++)
            {
                var url = $"https://hub.docker.com/v2/repositories/{repository}/tags?page_size=100&ordering=last_updated&page={page}";
                var resp = await http.GetFromJsonAsync<DockerHubTagsResponse>(url, ct);
                if (resp?.Results is null || resp.Results.Count == 0) break;
                names.AddRange(resp.Results.Select(r => r.Name).Where(n => !string.IsNullOrEmpty(n))!);
                if (string.IsNullOrEmpty(resp.Next)) break;
            }
            return names;
        }

        private async Task<IReadOnlyList<string>> FetchMcrTagsAsync(string repository, CancellationToken ct)
        {
            // MCR speaks the OCI registry API; tags/list returns every tag in one document.
            var resp = await http.GetFromJsonAsync<McrTagsResponse>($"https://mcr.microsoft.com/v2/{repository}/tags/list", ct);
            return resp?.Tags ?? new List<string>();
        }

        // The sort key is every integer group in the tag, compared element-wise descending. Works
        // across shapes: "18.4" -> [18,4], "v26.2.2" -> [26,2,2], "pg18"/"latest-pg18" -> [18],
        // "2025-latest" -> [2025]. A longer run with an equal prefix sorts higher, so "18.4" beats
        // the bare "18" rolling tag (patched build listed first).
        private static int[] NumericKey(string tag) =>
            DigitGroups().Matches(tag).Select(m => int.TryParse(m.Value, out var n) ? n : 0).ToArray();

        private sealed class IntSequenceComparer : IComparer<int[]>
        {
            public static readonly IntSequenceComparer Instance = new();

            public int Compare(int[]? x, int[]? y)
            {
                x ??= []; y ??= [];
                var len = Math.Min(x.Length, y.Length);
                for (var i = 0; i < len; i++)
                {
                    if (x[i] != y[i]) return x[i].CompareTo(y[i]);
                }
                return x.Length.CompareTo(y.Length);
            }
        }

        private sealed class IntSequenceEqualityComparer : IEqualityComparer<int[]>
        {
            public static readonly IntSequenceEqualityComparer Instance = new();

            public bool Equals(int[]? x, int[]? y) => (x ?? []).AsSpan().SequenceEqual(y ?? []);

            public int GetHashCode(int[] obj)
            {
                var hash = new HashCode();
                foreach (var i in obj) hash.Add(i);
                return hash.ToHashCode();
            }
        }

        private static Regex Rx(string pattern) => new(pattern, RegexOptions.Compiled);

        [GeneratedRegex(@"\d+")]
        private static partial Regex DigitGroups();

        private record DockerHubTagsResponse
        {
            [JsonPropertyName("next")] public string? Next { get; init; }
            [JsonPropertyName("results")] public List<DockerHubTag>? Results { get; init; }
        }

        private record DockerHubTag
        {
            [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        }

        private record McrTagsResponse
        {
            [JsonPropertyName("tags")] public List<string>? Tags { get; init; }
        }
    }
}

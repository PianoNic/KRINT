using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KRINT.Infrastructure.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace KRINT.Infrastructure.Services
{
    public class DatabaseVersionService : IDatabaseVersionService
    {
        private static readonly IReadOnlyDictionary<string, string> EngineToEndOfLifeSlug = new Dictionary<string, string>
        {
            ["postgres"] = "postgresql",
            ["mysql"] = "mysql",
            ["mariadb"] = "mariadb",
            ["mongo"] = "mongodb",
            ["redis"] = "redis",
            ["cockroachdb"] = "cockroachdb",
            ["clickhouse"] = "clickhouse",
            ["cassandra"] = "cassandra",
            ["elasticsearch"] = "elasticsearch",
            ["opensearch"] = "opensearch",
            ["neo4j"] = "neo4j",
            ["solr"] = "apache-solr",
            ["couchbase"] = "couchbase-server",
            // arangodb / influxdb / mssql / cockroachdb intentionally omitted - endoflife.date's
            // "latest" patch value doesn't map to a docker tag for these. See StaticEngineVersions.
        };

        // Hand-curated Docker tag lists for engines not on endoflife.date, or whose endoflife
        // "latest" doesn't match an actual Docker tag. All entries verified against
        // hub.docker.com / mcr.microsoft.com on 2026-05-22.
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> StaticEngineVersions = new Dictionary<string, IReadOnlyList<string>>
        {
            // timescale/timescaledb is published as `latest-pg<N>` per supported Postgres major.
            ["timescaledb"] = new[] { "latest-pg18", "latest-pg17", "latest-pg16", "latest-pg15" },
            // ScyllaDB switched to year-based versioning in 2025. 2026.1 is current stable.
            ["scylladb"] = new[] { "2026.1", "2026.1.3", "2025.4", "2025.4.8", "2025.1" },
            // CouchDB - 3.5 is current; 3.4 is the previous line.
            ["couchdb"] = new[] { "3.5.1", "3.5", "3.4.3", "3.4", "3.3" },
            // etcd lives on quay.io/coreos/etcd. v3.5 is the current supported branch.
            ["etcd"] = new[] { "v3.5.17", "v3.5.16", "v3.4.34" },
            // pgvector/pgvector image tracks Postgres majors.
            ["pgvector"] = new[] { "pg18", "pg17", "pg16", "pg15" },
            // Meilisearch - moves fast; v1.44 is current.
            ["meilisearch"] = new[] { "v1.44.0", "v1.44", "v1.43.1", "v1.43" },
            // Qdrant - v1.18 is current.
            ["qdrant"] = new[] { "v1.18.1", "v1.18", "v1" },
            // Valkey - Redis fork; 9.1 is current GA, 9 is the rolling major tag.
            ["valkey"] = new[] { "9.1.0", "9.1", "9", "8.1" },
            // CockroachDB image tags start with "v"; endoflife strips it. v26.2 is current GA.
            ["cockroachdb"] = new[] { "v26.2.0", "latest-v26.2", "v26.1.4", "v25.4.10", "v24.1.29" },
            // ArangoDB ships rolling minor tags (3.12) plus pinned patch tags.
            ["arangodb"] = new[] { "3.12", "3.12.9.1", "3.12.9", "3.11", "3.11.14" },
            // InfluxDB v2 lives on the `influxdb` image; v3 is the separate `influxdb3-core` image
            // and a different API shape, so we only surface v2 here. 2.9 is the current line.
            ["influxdb"] = new[] { "2.9", "2.9.1", "2.8", "2.7", "2.7.12" },
            // SQL Server uses Microsoft-curated marketing tags. 2025 is the current GA.
            ["mssql"] = new[] { "2025-latest", "2022-latest", "2019-latest" },
        };

        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        private readonly HttpClient _http;
        private readonly IMemoryCache _cache;

        public DatabaseVersionService(HttpClient http, IMemoryCache cache)
        {
            _http = http;
            _cache = cache;
        }

        public async Task<IReadOnlyList<string>> GetSupportedVersionsAsync(string engineKey, CancellationToken cancellationToken = default)
        {
            if (StaticEngineVersions.TryGetValue(engineKey, out var staticVersions))
            {
                return staticVersions;
            }

            if (!EngineToEndOfLifeSlug.TryGetValue(engineKey, out var slug))
            {
                throw new ArgumentException($"Unknown engine key '{engineKey}'.", nameof(engineKey));
            }

            var cacheKey = $"endoflife:{slug}";
            if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            {
                return cached;
            }

            var url = $"https://endoflife.date/api/{slug}.json";
            var entries = await _http.GetFromJsonAsync<EndOfLifeEntry[]>(url, cancellationToken);
            if (entries is null)
            {
                throw new InvalidOperationException($"endoflife.date returned no data for '{slug}'.");
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            // For each cycle still in support, surface both the latest patch tag and the major.
            // Latest patch first so the dropdown's natural top choice picks up CVE fixes; the
            // bare major (Docker's rolling tag) stays available for users who want it.
            var supported = entries
                .Where(e => IsStillSupported(e.Eol, today))
                .Where(e => !string.IsNullOrEmpty(e.Cycle))
                .SelectMany(e =>
                {
                    var latest = e.Latest;
                    if (!string.IsNullOrEmpty(latest) && !string.Equals(latest, e.Cycle, StringComparison.Ordinal))
                    {
                        return new[] { latest!, e.Cycle };
                    }
                    return new[] { e.Cycle };
                })
                .ToArray();

            _cache.Set(cacheKey, (IReadOnlyList<string>)supported, CacheDuration);
            return supported;
        }

        private static bool IsStillSupported(JsonElement eol, DateOnly today)
        {
            switch (eol.ValueKind)
            {
                case JsonValueKind.False:
                    return true;
                case JsonValueKind.String:
                    if (DateOnly.TryParse(eol.GetString(), out var eolDate))
                    {
                        return eolDate > today;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private record EndOfLifeEntry
        {
            [JsonPropertyName("cycle")]
            public string Cycle { get; init; } = string.Empty;

            [JsonPropertyName("latest")]
            public string? Latest { get; init; }

            [JsonPropertyName("eol")]
            public JsonElement Eol { get; init; }
        }
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KRINT.Infrastructure.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace KRINT.Infrastructure.Services
{
    public class DatabaseVersionService(HttpClient http, IMemoryCache cache) : IDatabaseVersionService
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
            ["neo4j"] = "neo4j",
            // mssql / cockroachdb intentionally omitted - endoflife.date's "latest" patch value
            // doesn't map to a docker tag for these. See StaticEngineVersions.
        };

        // Hand-curated Docker tag lists for engines not on endoflife.date, or whose endoflife
        // "latest" doesn't match an actual Docker tag. All entries verified against
        // hub.docker.com / mcr.microsoft.com on 2026-05-22.
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> StaticEngineVersions = new Dictionary<string, IReadOnlyList<string>>
        {
            // timescale/timescaledb is published as `latest-pg<N>` per supported Postgres major.
            ["timescaledb"] = new[] { "latest-pg18", "latest-pg17", "latest-pg16", "latest-pg15" },
            // CouchDB - 3.5 is current; 3.4 is the previous line.
            ["couchdb"] = new[] { "3.5.1", "3.5", "3.4.3", "3.4", "3.3" },
            // pgvector/pgvector image tracks Postgres majors.
            ["pgvector"] = new[] { "pg18", "pg17", "pg16", "pg15" },
            // Qdrant - v1.18 is current.
            ["qdrant"] = new[] { "v1.18.1", "v1.18", "v1" },
            // Valkey - Redis fork; 9.1 is current GA, 9 is the rolling major tag.
            ["valkey"] = new[] { "9.1.0", "9.1", "9", "8.1" },
            // CockroachDB image tags start with "v"; endoflife strips it. v26.2 is current GA.
            ["cockroachdb"] = new[] { "v26.2.0", "latest-v26.2", "v26.1.4", "v25.4.10", "v24.1.29" },
            // SQL Server uses Microsoft-curated marketing tags. 2025 is the current GA.
            ["mssql"] = new[] { "2025-latest", "2022-latest", "2019-latest" },
        };

        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

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
            if (cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            {
                return cached;
            }

            var url = $"https://endoflife.date/api/{slug}.json";
            var entries = await http.GetFromJsonAsync<EndOfLifeEntry[]>(url, cancellationToken);
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

            cache.Set(cacheKey, (IReadOnlyList<string>)supported, CacheDuration);
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

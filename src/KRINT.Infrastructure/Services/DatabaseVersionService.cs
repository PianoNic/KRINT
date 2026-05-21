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
            ["mongo"] = "mongodb",
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
            var supported = entries
                .Where(e => IsStillSupported(e.Eol, today))
                .Select(e => e.Cycle)
                .Where(c => !string.IsNullOrEmpty(c))
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

            [JsonPropertyName("eol")]
            public JsonElement Eol { get; init; }
        }
    }
}

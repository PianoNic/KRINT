using System.Text;
using System.Text.Json;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Infrastructure.Services
{
    // Scrolls a Qdrant collection's points WITH vectors (the browse path uses with_vector=false).
    // Reuses QdrantHttp (api-key auth) from QdrantServices.
    public class QdrantVectorService : IQdrantVectorService
    {
        public async Task<IReadOnlyList<VectorPoint>> FetchAsync(InnerDatabaseTarget target, string collection, int limit, CancellationToken cancellationToken = default)
        {
            InnerDatabaseNameValidator.Require(collection);
            limit = Math.Clamp(limit, 1, 2000);

            using var http = QdrantHttp.Build(target);
            var body = JsonSerializer.Serialize(new { limit, with_payload = true, with_vector = true });
            using var resp = await http.PostAsync(
                $"/collections/{Uri.EscapeDataString(collection)}/points/scroll",
                new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var points = new List<VectorPoint>();
            if (doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("points", out var pts))
            {
                foreach (var p in pts.EnumerateArray())
                {
                    var id = p.TryGetProperty("id", out var i) ? i.ToString() : null;
                    if (id is null) continue;
                    var vec = ExtractVector(p);
                    if (vec.Count == 0) continue; // skip points without a usable unnamed vector
                    var payload = p.TryGetProperty("payload", out var pl) ? pl.GetRawText() : "{}";
                    points.Add(new VectorPoint(id, vec, payload));
                }
            }
            return points;
        }

        // The "vector" field is an array for unnamed vectors, or an object {name: [...]} for named
        // ones. Take the array directly, or the first named vector's array.
        private static IReadOnlyList<float> ExtractVector(JsonElement point)
        {
            if (!point.TryGetProperty("vector", out var v)) return Array.Empty<float>();
            if (v.ValueKind == JsonValueKind.Array) return ToFloats(v);
            if (v.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in v.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.Array) return ToFloats(prop.Value);
            }
            return Array.Empty<float>();
        }

        private static float[] ToFloats(JsonElement arr)
        {
            var list = new List<float>(arr.GetArrayLength());
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Number) list.Add((float)e.GetDouble());
            return list.ToArray();
        }
    }
}

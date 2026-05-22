using System.Globalization;

namespace KRINT.Application.Options;

public sealed class KrintOptions
{
    public Dictionary<string, string> PortRanges { get; set; } = new();

    public PortRange GetPortRange(string engine)
    {
        if (!PortRanges.TryGetValue(engine, out var raw))
            throw new InvalidOperationException($"No port range configured for engine '{engine}'. Set krint.port_ranges.{engine} in krint.yaml.");

        return PortRange.Parse(engine, raw);
    }
}

public readonly record struct PortRange(int Start, int End)
{
    public static PortRange Parse(string engine, string raw)
    {
        var parts = raw.Split('-', 2);
        if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var end) || start < 1 || end > 65535 || start > end)
        {
            throw new InvalidOperationException($"Invalid port range '{raw}' for engine '{engine}'. Expected 'start-end' with 1 <= start <= end <= 65535.");
        }

        return new PortRange(start, end);
    }
}

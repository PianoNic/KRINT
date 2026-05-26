using System.Globalization;

namespace KRINT.Application.Options;

public sealed class KrintOptions
{
    public Dictionary<string, string> PortRanges { get; set; } = new();

    /// <summary>
    /// Controls how provisioned instances persist data. Defaults to per-instance named
    /// Docker volumes (krint-{slug}-data). Set to HostFolder + provide a HostPath to instead
    /// bind-mount each instance's data directory under {HostPath}/{containerName} on the host.
    /// Existing instances keep whatever they were originally provisioned with — switching
    /// modes only affects future provisions.
    /// </summary>
    public StorageOptions Storage { get; set; } = new();

    /// <summary>
    /// Optional path (relative to krint.yaml or absolute) to a YAML file that declares
    /// instances KRINT should ensure exist on startup. When set, the reconcile hosted
    /// service reads it, provisions missing entries, and flags them IsConfigManaged so the
    /// UI hides mutation controls. Removing an entry and restarting clears the flag.
    /// </summary>
    public string? InstancesFile { get; set; }

    public PortRange GetPortRange(string engine)
    {
        if (!PortRanges.TryGetValue(engine, out var raw))
            throw new InvalidOperationException($"No port range configured for engine '{engine}'. Set krint.port_ranges.{engine} in krint.yaml.");

        return PortRange.Parse(engine, raw);
    }
}

public enum StorageMode
{
    Volume,
    HostFolder,
}

public sealed class StorageOptions
{
    public StorageMode Mode { get; set; } = StorageMode.Volume;

    /// <summary>
    /// Required when Mode = HostFolder. Absolute path on the Docker host (not inside the
    /// KRINT container) under which a subdirectory will be created per provisioned instance.
    /// Forward slashes work on Windows hosts too because dockerd normalises them.
    /// </summary>
    public string? HostPath { get; set; }

    public string ResolveBindForContainer(string containerName, string dataPath)
    {
        return Mode switch
        {
            StorageMode.HostFolder => $"{ResolveHostPath()}/{containerName}:{dataPath}",
            _ => $"{containerName}-data:{dataPath}",
        };
    }

    public string? ResolveHostFolderForContainer(string containerName)
    {
        return Mode == StorageMode.HostFolder ? $"{ResolveHostPath()}/{containerName}" : null;
    }

    /// <summary>
    /// Non-throwing variant used by cleanup paths (delete/rollback) which run even when the
    /// instance was originally provisioned under a different storage mode. Returns null both
    /// when the current mode is Volume and when HostFolder is misconfigured.
    /// </summary>
    public string? TryResolveHostFolderForContainer(string containerName)
    {
        if (Mode != StorageMode.HostFolder || string.IsNullOrWhiteSpace(HostPath)) return null;
        return $"{HostPath.TrimEnd('/', '\\')}/{containerName}";
    }

    private string ResolveHostPath()
    {
        if (string.IsNullOrWhiteSpace(HostPath))
            throw new InvalidOperationException("storage.mode is HostFolder but storage.host_path is not set in krint.yaml.");
        return HostPath.TrimEnd('/', '\\');
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

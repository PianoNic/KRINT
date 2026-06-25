namespace KRINT.API.Nodes
{
    // KRINT-owned wire contracts for node RPC. Deliberately NOT the Docker.DotNet model types:
    // those carry Newtonsoft.Json attributes that SignalR's System.Text.Json ignores, so a raw
    // CreateContainerParameters would lose its PortBindings/ExposedPorts in transit. These plain
    // records round-trip cleanly; the node rebuilds the Docker parameters from them locally.

    public record PortBindingSpec(string ContainerPort, string HostPort, string HostIp);

    /// <summary>Everything KRINT sets when creating a container, in a transport-safe shape.</summary>
    public record CreateContainerSpec(
        string Image,
        string Name,
        List<string> Env,
        List<string>? Cmd,
        List<string> ExposedPorts,
        List<PortBindingSpec> PortBindings,
        List<string> Binds,
        Dictionary<string, string> Labels,
        string RestartPolicy);
}

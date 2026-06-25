namespace KRINT.API.Nodes
{
    /// <summary>What a node reports about itself when it dials in and registers. Id is the node's
    /// stable Node:Id (a GUID, as string over the wire) so the control plane can persist it and let
    /// instances reference the node across reconnects.</summary>
    public record NodeRegistrationDto(string Id, string Name, string MachineName, string Os, string DockerVersion);

    /// <summary>A node as the control-plane UI sees it. Id is the stable node id; Online is derived
    /// from the live SignalR registry, the rest from the persisted Node row.</summary>
    public record NodeDto(
        Guid Id,
        string Name,
        string MachineName,
        string Os,
        string DockerVersion,
        bool Online,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt);

    /// <summary>Result of a control-plane -> node round-trip ping.</summary>
    public record NodePingResultDto(string Reply, int RoundTripMs);
}

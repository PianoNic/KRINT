namespace KRINT.API.Nodes
{
    /// <summary>What a node reports about itself when it dials in and registers. Phase 1 is purely
    /// informational - routing provisioning to a node comes later.</summary>
    public record NodeRegistrationDto(string Name, string MachineName, string Os, string DockerVersion);

    /// <summary>A node as the control-plane UI sees it. Id is the live SignalR connection id (phase 1
    /// has no persisted node identity), so it changes across reconnects.</summary>
    public record NodeDto(
        string ConnectionId,
        string Name,
        string MachineName,
        string Os,
        string DockerVersion,
        bool Online,
        DateTimeOffset ConnectedAt,
        DateTimeOffset LastSeenAt);

    /// <summary>Result of a control-plane -> node round-trip ping.</summary>
    public record NodePingResultDto(string Reply, int RoundTripMs);
}

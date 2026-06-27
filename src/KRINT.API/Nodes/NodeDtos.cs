namespace KRINT.API.Nodes
{
    /// <summary>What a node reports about itself when it dials in and registers. Id is the node's
    /// stable Node:Id (a GUID, as string over the wire) so the control plane can persist it and let
    /// instances reference the node across reconnects.</summary>
    public record NodeRegistrationDto(string Id, string Name, string MachineName, string Os, string DockerVersion);

    /// <summary>A node as the control-plane UI sees it. Id is the stable node id; Online is derived
    /// from the live SignalR registry, the rest from the persisted Node row. Pending is true for a
    /// node that's been created/declared but has never connected (empty runtime details).</summary>
    public record NodeDto(
        Guid Id,
        string Name,
        string MachineName,
        string Os,
        string DockerVersion,
        bool Online,
        bool Pending,
        bool IsConfigManaged,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt);

    /// <summary>Result of a control-plane -> node round-trip ping.</summary>
    public record NodePingResultDto(string Reply, int RoundTripMs);

    /// <summary>A not-yet-saved node: a freshly generated token + the control-plane URL the node
    /// should dial. The UI builds the compose from these and only persists on save. ControlPlaneUrl
    /// is null when Krint:PublicUrl isn't configured (the UI warns and uses a placeholder).</summary>
    public record NodeDraftDto(string SuggestedName, string Token, string? ControlPlaneUrl);

    /// <summary>Persist a node from the Add-node modal. The token is the one shown in the draft;
    /// only its hash is stored.</summary>
    public record CreateNodeRequest(string Name, string Token);
}

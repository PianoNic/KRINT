namespace KRINT.API.Nodes
{
    /// <summary>What a node reports about itself when it dials in and registers. Id is the node's
    /// stable Node:Id (a GUID, as string over the wire) so the control plane can persist it and let
    /// instances reference the node across reconnects. ProtocolVersion is <see cref="NodeProtocol.Current"/>
    /// of the agent; agents that predate the field deserialize as 0.</summary>
    public record NodeRegistrationDto(string Id, string Name, string MachineName, string Os, string DockerVersion, int ProtocolVersion = 0);

    /// <summary>Version of the control plane - node RPC contract. Bumped when a node-side handler the
    /// control plane relies on is added or changed, so a mismatch fails with a clear upgrade message
    /// instead of an opaque missing-handler error.</summary>
    public static class NodeProtocol
    {
        /// <summary>1 = single-message RPC. 2 = backup/restore dumps move over the chunked
        /// <c>/api/nodes/transfers</c> HTTP endpoint (ExecCaptureUpload / ExecWithStdinDownload).</summary>
        public const int Current = 2;

        /// <summary>Lowest agent protocol that supports streamed dump transfer.</summary>
        public const int StreamedTransfer = 2;
    }

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
    public record NodeDraftDto(string SuggestedName, string Token, string ControlPlaneUrl);

    /// <summary>Persist a node from the Add-node modal. The token is the one shown in the draft;
    /// only its hash is stored.</summary>
    public record CreateNodeRequest(string Name, string Token);
}

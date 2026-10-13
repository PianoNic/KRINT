namespace KRINT.Application.Dtos.Database
{
    public record ProvisionedDatabaseDto
    {
        public required Guid Id { get; init; }
        public required string Engine { get; init; }
        public required string Version { get; init; }
        /// <summary>Null for externally-registered instances.</summary>
        public string? ContainerName { get; init; }
        public required string Host { get; init; }
        public required int Port { get; init; }
        public required string Username { get; init; }
        public required string DatabaseName { get; init; }
        public required string Password { get; init; }
        public required string ConnectionString { get; init; }
        public required DateTime CreatedAt { get; init; }
        public required bool IsManaged { get; init; }
        public required bool IsPublic { get; init; }
        public string? State { get; init; }
        public required bool IsConfigManaged { get; init; }
        /// <summary>The node this instance's container runs on, or null for the control plane's
        /// local Docker daemon.</summary>
        public Guid? NodeId { get; init; }
        /// <summary>Display name of that node, so the UI can say where the instance lives instead
        /// of showing a "localhost" that means nothing to the caller.</summary>
        public string? NodeName { get; init; }
        /// <summary>The engine's in-container port. Pairs with ContainerName on a shared Docker
        /// network - the route that works no matter how the host port is bound.</summary>
        public int? ContainerPort { get; init; }
        /// <summary>Connection string against ContainerName:ContainerPort. For a node-hosted
        /// instance this is the only endpoint another container can use, because Host:Port is
        /// published on the node's loopback.</summary>
        public string? ContainerConnectionString { get; init; }
        /// <summary>A joinable Docker network the container is attached to, when we could inspect
        /// it. This is what a user's own compose service has to declare as external to reach the
        /// instance by container name.</summary>
        public string? DockerNetwork { get; init; }
        /// <summary>True when the published port is bound to 127.0.0.1 only, so nothing outside
        /// the container's own host can dial it.</summary>
        public bool LoopbackOnly { get; init; }
        /// <summary>The host an app's container on the same machine uses to reach this instance
        /// without joining any network: host.docker.internal on Docker Desktop, the default bridge
        /// gateway on Linux. Null when unknown or not applicable (external instances).</summary>
        public string? AppHost { get; init; }
        /// <summary>Connection string for apps running in containers on the same host, built from
        /// <see cref="AppHost"/> and the published port. The one to paste into a compose file.</summary>
        public string? AppConnectionString { get; init; }
        /// <summary>True when this host needs the bridge binding but the container predates it, so
        /// <see cref="AppConnectionString"/> only works after the container is recreated.</summary>
        public bool AppAccessNeedsRecreate { get; init; }
    }
}

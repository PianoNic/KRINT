using KRINT.Application.Dtos.Database;

namespace KRINT.Application.Mappings.Database
{
    public static class ProvisionedDatabaseMappings
    {
        /// <param name="nodeName">Display name of the node hosting this instance, when node-hosted.</param>
        /// <param name="networks">Docker networks the container is attached to, when the caller
        /// could inspect it. Used to suggest the network a user's own service must join.</param>
        public static ProvisionedDatabaseDto ToProvisionedDto(
            this KRINT.Domain.DatabaseInstance instance,
            string password,
            string connectionString,
            string? nodeName = null,
            IEnumerable<string>? networks = null)
        {
            var internalPort = InstanceReachability.InternalPortFor(instance.Engine);

            // Only containers KRINT knows by name can be addressed on a shared network; externally
            // registered instances have no container to point at.
            var containerConnectionString = instance.ContainerName is not null && internalPort is { } port
                ? ConnectionStringBuilder.Build(instance.Engine, instance.ContainerName, port, instance.Username, password, instance.DatabaseName)
                : null;

            return new()
            {
                Id = instance.Id,
                Engine = instance.Engine,
                Version = instance.Version,
                ContainerName = instance.ContainerName,
                Host = instance.Host,
                Port = instance.Port,
                Username = instance.Username,
                DatabaseName = instance.DatabaseName,
                Password = password,
                ConnectionString = connectionString,
                CreatedAt = instance.CreatedAt,
                IsManaged = instance.IsManaged,
                IsPublic = instance.IsPublic,
                IsConfigManaged = instance.IsConfigManaged,
                NodeId = instance.NodeId,
                NodeName = nodeName,
                ContainerPort = internalPort,
                ContainerConnectionString = containerConnectionString,
                DockerNetwork = InstanceReachability.PickSharedNetwork(networks),
                LoopbackOnly = InstanceReachability.IsLoopbackOnly(instance),
            };
        }
    }
}

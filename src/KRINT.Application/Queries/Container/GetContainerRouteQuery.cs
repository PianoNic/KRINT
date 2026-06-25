using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;

namespace KRINT.Application.Queries.Container
{
    /// <summary>Where an instance's container lives: its id plus the node it runs on (null = local).
    /// Used by the container hub to decide whether to talk to the local Docker daemon or relay to a node.</summary>
    public record ContainerRoute(string ContainerId, Guid? NodeId);

    public record GetContainerRouteQuery(Guid InstanceId) : IQuery<ContainerRoute>;

    public class GetContainerRouteQueryHandler(KrintDbContext db) : IQueryHandler<GetContainerRouteQuery, ContainerRoute>
    {
        public async ValueTask<ContainerRoute> Handle(GetContainerRouteQuery query, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == query.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(query.InstanceId);

            var containerId = instance.ContainerId
                ?? throw new InvalidOperationException("Container operations are not available for externally-registered databases.");

            return new ContainerRoute(containerId, instance.NodeId);
        }
    }
}

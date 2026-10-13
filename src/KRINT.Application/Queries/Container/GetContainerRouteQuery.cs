using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Container
{
    /// <summary>Where an instance's container lives: its id plus the node it runs on (null = local).
    /// Used by the container hub to decide whether to talk to the local Docker daemon or relay to a node.</summary>
    public record ContainerRoute(string ContainerId, Guid? NodeId);

    public record GetContainerRouteQuery(Guid InstanceId) : IQuery<ContainerRoute>;

    public class GetContainerRouteQueryHandler(KrintDbContext db, IDockerServiceResolver dockerResolver) : IQueryHandler<GetContainerRouteQuery, ContainerRoute>
    {
        public async ValueTask<ContainerRoute> Handle(GetContainerRouteQuery query, CancellationToken cancellationToken)
        {
            // Tracked: a stale id (compose recreated the container) is healed and saved below.
            var instance = await db.DatabaseInstances
                .FirstOrDefaultAsync(d => d.Id == query.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(query.InstanceId);

            if (instance.ContainerId is null)
                throw new InvalidOperationException("Container operations are not available for externally-registered databases.");

            await ContainerIdResolver.RefreshAsync(db, instance, dockerResolver.Resolve(instance.NodeId), cancellationToken);

            return new ContainerRoute(instance.ContainerId, instance.NodeId);
        }
    }
}

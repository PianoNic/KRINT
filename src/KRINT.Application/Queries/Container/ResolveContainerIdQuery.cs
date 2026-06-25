using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Infrastructure;

namespace KRINT.Application.Queries.Container
{
    public record ResolveContainerIdQuery(Guid InstanceId) : IQuery<string>;

    public class ResolveContainerIdQueryHandler(KrintDbContext db)
        : IQueryHandler<ResolveContainerIdQuery, string>
    {
        public async ValueTask<string> Handle(ResolveContainerIdQuery query, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == query.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(query.InstanceId);
            NodeFeatureGuard.EnsureLocal(instance, "The interactive console");
            return instance.ContainerId
                ?? throw new InvalidOperationException("Container operations are not available for externally-registered databases.");
        }
    }
}

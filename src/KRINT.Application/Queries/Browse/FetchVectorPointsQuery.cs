using Mediator;
using KRINT.Application.Dtos.Browse;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Browse
{
    public record FetchVectorPointsQuery(Guid InstanceId, string Collection, int Limit = 500)
        : IQuery<VectorClusterDto>;

    public class FetchVectorPointsQueryHandler(KrintDbContext db, ISecretsVaultService vault, IQdrantVectorService qdrant)
        : IQueryHandler<FetchVectorPointsQuery, VectorClusterDto>
    {
        public async ValueTask<VectorClusterDto> Handle(FetchVectorPointsQuery query, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, query.InstanceId, cancellationToken);
            if (!string.Equals(target.Engine, "qdrant", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Cluster view is only available for Qdrant instances.");

            var points = await qdrant.FetchAsync(target, query.Collection, query.Limit, cancellationToken);
            return new VectorClusterDto
            {
                Dimensions = points.Count > 0 ? points[0].Vector.Count : 0,
                Points = points.Select(p => new VectorPointDto { Id = p.Id, Vector = p.Vector, Payload = p.Payload }).ToList(),
            };
        }
    }
}

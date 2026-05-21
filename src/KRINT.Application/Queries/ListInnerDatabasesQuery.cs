using Mediator;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries
{
    public record ListInnerDatabasesQuery(Guid InstanceId) : IQuery<IReadOnlyList<string>>;

    public class ListInnerDatabasesQueryHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerDatabaseServiceResolver resolver)
        : IQueryHandler<ListInnerDatabasesQuery, IReadOnlyList<string>>
    {
        public async ValueTask<IReadOnlyList<string>> Handle(ListInnerDatabasesQuery query, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, query.InstanceId, cancellationToken);
            return await resolver.Resolve(target.Engine).ListAsync(target, cancellationToken);
        }
    }
}

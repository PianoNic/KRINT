using Mediator;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries
{
    public record ListInnerUsersQuery(Guid InstanceId) : IQuery<IReadOnlyList<string>>;

    public class ListInnerUsersQueryHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerUserServiceResolver resolver)
        : IQueryHandler<ListInnerUsersQuery, IReadOnlyList<string>>
    {
        public async ValueTask<IReadOnlyList<string>> Handle(ListInnerUsersQuery query, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, query.InstanceId, cancellationToken);
            return await resolver.Resolve(target.Engine).ListAsync(target, cancellationToken);
        }
    }
}

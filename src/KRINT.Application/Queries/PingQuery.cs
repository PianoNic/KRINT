using Mediator;

namespace KRINT.Application.Queries
{
    public record PingQuery : IQuery<string>;

    public class PingQueryHandler : IQueryHandler<PingQuery, string>
    {
        public ValueTask<string> Handle(PingQuery query, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult("pong");
        }
    }
}

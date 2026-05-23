using Mediator;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Container
{
    public record WriteContainerExecInputCommand(string SessionId, byte[] Data) : ICommand;

    public class WriteContainerExecInputCommandHandler(IContainerExecRegistry registry)
        : ICommandHandler<WriteContainerExecInputCommand>
    {
        public async ValueTask<Unit> Handle(WriteContainerExecInputCommand command, CancellationToken cancellationToken)
        {
            var session = registry.Get(command.SessionId);
            if (session is null) return Unit.Value;
            await session.WriteAsync(command.Data, cancellationToken);
            return Unit.Value;
        }
    }
}

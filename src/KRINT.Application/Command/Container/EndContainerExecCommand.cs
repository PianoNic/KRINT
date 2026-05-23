using Mediator;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Container
{
    public record EndContainerExecCommand(string SessionId) : ICommand;

    public class EndContainerExecCommandHandler(IContainerExecRegistry registry)
        : ICommandHandler<EndContainerExecCommand>
    {
        public async ValueTask<Unit> Handle(EndContainerExecCommand command, CancellationToken cancellationToken)
        {
            await registry.EndAsync(command.SessionId);
            return Unit.Value;
        }
    }
}

using Mediator;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Container
{
    public record ResizeContainerExecCommand(string SessionId, uint Cols, uint Rows) : ICommand;

    public class ResizeContainerExecCommandHandler(IContainerExecRegistry registry)
        : ICommandHandler<ResizeContainerExecCommand>
    {
        public async ValueTask<Unit> Handle(ResizeContainerExecCommand command, CancellationToken cancellationToken)
        {
            var session = registry.Get(command.SessionId);
            if (session is null) return Unit.Value;
            try { await session.ResizeAsync(command.Cols, command.Rows, cancellationToken); } catch { /* daemon hiccup */ }
            return Unit.Value;
        }
    }
}

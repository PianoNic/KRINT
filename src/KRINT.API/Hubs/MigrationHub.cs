using System.Runtime.CompilerServices;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using KRINT.Application.Command.Migration;
using KRINT.Application.Dtos.Migration;

namespace KRINT.API.Hubs
{
    /// <summary>SignalR endpoint backing the guided migration wizard. One stream per migration.
    /// Cancellation comes from the client unsubscribing (or the connection dropping) and is
    /// propagated through to the dump/restore so a half-finished job doesn't hang the target.</summary>
    [Authorize]
    public class MigrationHub(IMediator mediator) : Hub
    {
        public async IAsyncEnumerable<MigrationProgressDto> StreamMigration(
            MigrationRequestDto request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var ev in mediator.CreateStream(new StreamMigrateContainerCommand(request), cancellationToken))
            {
                yield return ev;
            }
        }
    }
}

using System.Runtime.CompilerServices;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using KRINT.Application.Command.Provision;
using KRINT.Application.Dtos.Provision;

namespace KRINT.API.Hubs
{
    /// <summary>SignalR endpoint backing the create wizard: one stream per provision, each event a
    /// step the server is on, ending with the provisioned instance or the reason it failed. The
    /// plain POST /api/database/provision stays for scripts and the declarative reconciler.</summary>
    [Authorize]
    public class ProvisionHub(IMediator mediator) : Hub
    {
        public async IAsyncEnumerable<ProvisionProgressDto> StreamProvision(
            ProvisionRequestDto request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var ev in mediator.CreateStream(new StreamProvisionDatabaseCommand(request), cancellationToken))
            {
                yield return ev;
            }
        }
    }
}

using Mediator;
using KRINT.Application.Dtos.InnerUser;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.InnerUser
{
    public record ResetInnerUserPasswordCommand(Guid InstanceId, string Name) : ICommand<InnerUserPasswordDto>;

    public class ResetInnerUserPasswordCommandHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerUserServiceResolver resolver,
        ISecretGeneratorService secretGenerator)
        : ICommandHandler<ResetInnerUserPasswordCommand, InnerUserPasswordDto>
    {
        public async ValueTask<InnerUserPasswordDto> Handle(ResetInnerUserPasswordCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            var password = secretGenerator.Generate();
            await resolver.Resolve(target.Engine).ResetPasswordAsync(target, command.Name, password, cancellationToken);
            return new InnerUserPasswordDto { Name = command.Name, Password = password };
        }
    }
}

using Mediator;
using KRINT.Application.Dtos;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command
{
    public record CreateInnerUserCommand(Guid InstanceId, string Name) : ICommand<InnerUserPasswordDto>;

    public class CreateInnerUserCommandHandler(
        KrintDbContext db,
        ISecretsVaultService vault,
        IInnerUserServiceResolver resolver,
        ISecretGeneratorService secretGenerator)
        : ICommandHandler<CreateInnerUserCommand, InnerUserPasswordDto>
    {
        public async ValueTask<InnerUserPasswordDto> Handle(CreateInnerUserCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            var password = secretGenerator.Generate();
            await resolver.Resolve(target.Engine).CreateAsync(target, command.Name, password, cancellationToken);
            return new InnerUserPasswordDto { Name = command.Name, Password = password };
        }
    }
}

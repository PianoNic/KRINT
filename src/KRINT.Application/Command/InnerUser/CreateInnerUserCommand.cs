using Mediator;
using KRINT.Application.Dtos.InnerUser;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;

namespace KRINT.Application.Command.InnerUser
{
    public record CreateInnerUserCommand(Guid InstanceId, string Name, string? Password = null) : ICommand<InnerUserPasswordDto>;

    public class CreateInnerUserCommandHandler(KrintDbContext db, ISecretsVaultService vault, IInnerUserServiceResolver resolver, ISecretGeneratorService secretGenerator, ConfigManagedGuard guard) : ICommandHandler<CreateInnerUserCommand, InnerUserPasswordDto>
    {
        public async ValueTask<InnerUserPasswordDto> Handle(CreateInnerUserCommand command, CancellationToken cancellationToken)
        {
            await guard.EnsureMutableAsync(db, command.InstanceId, cancellationToken);
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);

            string password;
            if (!string.IsNullOrEmpty(command.Password))
            {
                SafePasswordGuard.Require(command.Password);
                password = command.Password;
            }
            else
            {
                password = secretGenerator.Generate();
            }

            await resolver.Resolve(target.Engine).CreateAsync(target, command.Name, password, cancellationToken);
            return new InnerUserPasswordDto { Name = command.Name, Password = password };
        }
    }
}

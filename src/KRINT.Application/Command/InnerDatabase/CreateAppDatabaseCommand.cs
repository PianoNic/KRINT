using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Command.InnerUser;
using KRINT.Application.Dtos.Database;
using KRINT.Application.Queries.InnerDatabase;
using KRINT.Application.Queries.InnerUser;
using KRINT.Application.Queries.SupportedDatabase;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.InnerDatabase
{
    /// <summary>"Give my app a database": creates a database named after the app and, where the
    /// engine has users, a user of the same name that can only reach it. Returns the connection
    /// string an app on the same host pastes into its compose file.</summary>
    public record CreateAppDatabaseCommand(Guid InstanceId, string Name) : ICommand<AppDatabaseDto>;

    public class CreateAppDatabaseCommandHandler(
        KrintDbContext db,
        IMediator mediator,
        ISecretsVaultService vault,
        IDockerServiceResolver dockerResolver)
        : ICommandHandler<CreateAppDatabaseCommand, AppDatabaseDto>
    {
        public async ValueTask<AppDatabaseDto> Handle(CreateAppDatabaseCommand command, CancellationToken cancellationToken)
        {
            var instance = await db.DatabaseInstances.AsNoTracking().FirstOrDefaultAsync(d => d.Id == command.InstanceId, cancellationToken)
                ?? throw new InstanceNotFoundException(command.InstanceId);

            var name = command.Name?.Trim() ?? "";
            var caps = GetSupportedDatabasesQueryHandler.CapabilitiesFor(instance.Engine);
            if (caps is null || !caps.SupportsCreateDatabase)
                throw new NotSupportedException(
                    $"{instance.Engine} has no separate databases per app. Use the instance's own connection string.");

            // Checked up front so a taken name is a clear conflict, not the engine's raw error, and
            // so the rollback below never drops a database that belonged to someone else.
            var existing = await mediator.Send(new ListInnerDatabasesQuery(instance.Id), cancellationToken);
            if (existing.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"A {caps.DatabaseTerm} named '{name}' already exists on this instance.");
            if (caps.SupportsUsers)
            {
                var users = await mediator.Send(new ListInnerUsersQuery(instance.Id), cancellationToken);
                if (users.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"A user named '{name}' already exists on this instance.");
            }

            await mediator.Send(new CreateInnerDatabaseCommand(instance.Id, name), cancellationToken);

            string username, password;
            if (caps.SupportsUsers)
            {
                try
                {
                    var user = await mediator.Send(new CreateInnerUserCommand(instance.Id, name), cancellationToken);
                    await mediator.Send(new GrantInnerUserAccessCommand(instance.Id, name, name), cancellationToken);
                    (username, password) = (user.Name, user.Password);
                }
                catch
                {
                    // All or nothing: a database without its user is not what was asked for.
                    try { await mediator.Send(new DropInnerDatabaseCommand(instance.Id, name), CancellationToken.None); } catch { }
                    throw;
                }
            }
            else
            {
                username = instance.Username;
                password = await vault.RetrieveAsync(ConnectionStringBuilder.VaultKeyFor(instance), cancellationToken)
                    ?? throw new InvalidOperationException($"Vault has no password for instance {instance.Id}.");
            }

            return new AppDatabaseDto
            {
                Database = name,
                Username = username,
                Password = password,
                HasScopedUser = caps.SupportsUsers,
                AppConnectionString = await AppHostAsync(instance, cancellationToken) is { } appHost
                    ? ConnectionStringBuilder.Build(instance.Engine, appHost, instance.Port, username, password, name)
                    : null,
                ConnectionString = ConnectionStringBuilder.Build(instance.Engine, instance.Host, instance.Port, username, password, name),
            };
        }

        private async Task<string?> AppHostAsync(KRINT.Domain.DatabaseInstance instance, CancellationToken cancellationToken)
        {
            if (instance.ContainerId is null) return null;
            try
            {
                var docker = dockerResolver.Resolve(instance.NodeId);
                var inspect = await docker.InspectContainerAsync(instance.ContainerId, cancellationToken);
                var access = await docker.GetHostAccessAsync(cancellationToken);
                return InstanceReachability.AppAccess(instance, access, inspect.HostConfig?.PortBindings).AppHost;
            }
            catch
            {
                return null;   // the database exists either way; the UI falls back to the shell string
            }
        }
    }
}

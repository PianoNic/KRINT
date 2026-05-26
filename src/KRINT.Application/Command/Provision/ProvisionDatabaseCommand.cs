using Mediator;
using KRINT.Application.Command.Database;
using KRINT.Application.Command.InnerDatabase;
using KRINT.Application.Command.InnerUser;
using KRINT.Application.Dtos.InnerUser;
using KRINT.Application.Dtos.Provision;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command.Provision
{
    public record ProvisionDatabaseCommand(ProvisionRequestDto Request) : ICommand<ProvisionResultDto>;

    public class ProvisionDatabaseCommandHandler(IMediator mediator, IActivityLogger activity)
        : ICommandHandler<ProvisionDatabaseCommand, ProvisionResultDto>
    {
        public async ValueTask<ProvisionResultDto> Handle(ProvisionDatabaseCommand command, CancellationToken cancellationToken)
        {
            var req = command.Request;

            // 1. Create the instance (provisions the container, opens host port, stores root password).
            // Plugins propagate down so CreateDatabaseCommand can swap the image, set env vars,
            // and/or run post-readiness install steps.
            var instance = await mediator.Send(new CreateDatabaseCommand(req.Engine, req.Version, req.DisplayName, req.DefaultDatabaseName, req.Plugins, req.IsPublic, req.Password), cancellationToken);

            var createdDatabases = new List<string>();
            var createdUsers = new List<InnerUserPasswordDto>();

            // 2. Create the extra logical databases inside the instance.
            foreach (var name in req.Databases.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(name, instance.DatabaseName, StringComparison.OrdinalIgnoreCase))
                    continue; // already created as the default

                await mediator.Send(new CreateInnerDatabaseCommand(instance.Id, name), cancellationToken);
                createdDatabases.Add(name);
            }

            // Validate grants against the actually-created database set (default + extras),
            // not what the request *asked for*. This lets the client pass the engine-default
            // name (postgres / mysql / admin) even when DefaultDatabaseName was left blank.
            var availableDbs = new HashSet<string>(createdDatabases.Append(instance.DatabaseName), StringComparer.OrdinalIgnoreCase);
            ValidateUserGrants(req, availableDbs);

            // 3. Create users + grant access to each requested database.
            foreach (var user in req.Users)
            {
                var credential = await mediator.Send(new CreateInnerUserCommand(instance.Id, user.Name, user.Password), cancellationToken);
                createdUsers.Add(credential);

                foreach (var db in user.GrantDatabases.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    await mediator.Send(new GrantInnerUserAccessCommand(instance.Id, user.Name, db), cancellationToken);
                }
            }

            // Always include the default DB in the returned list.
            createdDatabases.Insert(0, instance.DatabaseName);

            // Provisioning always produces a managed container, so ContainerName is non-null here.
            await activity.LogAsync("provision.complete", instance.ContainerName!, instance.Id, instance.Engine, $"databases={createdDatabases.Count}, users={createdUsers.Count}", cancellationToken);

            return new ProvisionResultDto
            {
                Instance = instance,
                Databases = createdDatabases,
                Users = createdUsers,
            };
        }

        private static void ValidateUserGrants(ProvisionRequestDto req, HashSet<string> availableDatabases)
        {
            foreach (var user in req.Users)
            {
                foreach (var db in user.GrantDatabases)
                {
                    if (string.IsNullOrEmpty(db)) continue;
                    if (!availableDatabases.Contains(db))
                    {
                        throw new ArgumentException($"User '{user.Name}' grants reference unknown database '{db}'. " + $"Available: {string.Join(", ", availableDatabases)}.");
                    }
                }
            }
        }
    }
}

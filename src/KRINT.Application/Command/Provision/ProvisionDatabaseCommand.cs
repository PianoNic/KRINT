using Mediator;
using KRINT.Application.Command.Database;
using KRINT.Application.Command.InnerDatabase;
using KRINT.Application.Command.InnerUser;
using KRINT.Application.Dtos.InnerUser;
using KRINT.Application.Dtos.Provision;
using KRINT.Infrastructure.Interfaces;
using KRINT.Infrastructure.Services;

namespace KRINT.Application.Command.Provision
{
    public record ProvisionDatabaseCommand(ProvisionRequestDto Request, IProgress<string>? Progress = null) : ICommand<ProvisionResultDto>;

    public class ProvisionDatabaseCommandHandler(IMediator mediator, IActivityLogger activity)
        : ICommandHandler<ProvisionDatabaseCommand, ProvisionResultDto>
    {
        // Engines whose inner-database service refuses CreateAsync (fixed or virtual databases).
        private static readonly HashSet<string> NoExtraDatabases = new(StringComparer.OrdinalIgnoreCase)
        {
            "redis", "valkey", "neo4j", "qdrant",
        };

        // Engines whose inner-user service does not expose user management.
        private static readonly HashSet<string> NoUsers = new(StringComparer.OrdinalIgnoreCase)
        {
            "redis", "valkey", "neo4j", "qdrant", "cassandra", "couchdb", "seaweedfs", "azurite",
        };

        private static readonly HashSet<string> BucketEngines = new(StringComparer.OrdinalIgnoreCase)
        {
            "seaweedfs", "azurite",
        };

        public async ValueTask<ProvisionResultDto> Handle(ProvisionDatabaseCommand command, CancellationToken cancellationToken)
        {
            var req = command.Request;

            // 0. Validate everything that can be checked without a container BEFORE creating one,
            // so a typo in a database, user or grant never leaves an orphaned instance behind.
            var displayName = ValidateRequest(req);

            // 1. Create the instance (provisions the container, opens host port, stores root password).
            // Plugins propagate down so CreateDatabaseCommand can swap the image, set env vars,
            // and/or run post-readiness install steps.
            var instance = await mediator.Send(new CreateDatabaseCommand(
                Engine: req.Engine,
                Version: req.Version,
                DisplayName: displayName,
                DatabaseName: req.DefaultDatabaseName,
                Plugins: req.Plugins,
                IsPublic: req.IsPublic,
                Password: req.Password,
                NodeId: req.NodeId,
                Progress: command.Progress), cancellationToken);

            // From here on the instance exists and is usable. A failing extra step is reported as
            // a warning on an otherwise successful result instead of failing the whole provision:
            // the user keeps the instance (an image pull can take minutes) and is told exactly what
            // to add from the instance page. Deleting it would throw that work away, and reporting
            // "failed" while leaving it running is the orphan this replaces.
            var createdDatabases = new List<string>();
            var createdUsers = new List<InnerUserPasswordDto>();
            var warnings = new List<string>();

            // 2. Create the extra logical databases inside the instance.
            foreach (var name in req.Databases.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(name, instance.DatabaseName, StringComparison.OrdinalIgnoreCase))
                    continue; // already created as the default

                command.Progress?.Report($"Creating database {name}");
                try
                {
                    await mediator.Send(new CreateInnerDatabaseCommand(instance.Id, name), cancellationToken);
                    createdDatabases.Add(name);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    Warn(command, warnings, $"Database '{name}' could not be created: {ex.Message}");
                }
            }

            // 3. Create users + grant access to each requested database.
            foreach (var user in req.Users)
            {
                command.Progress?.Report($"Creating user {user.Name}");
                try
                {
                    var credential = await mediator.Send(new CreateInnerUserCommand(instance.Id, user.Name, user.Password), cancellationToken);
                    createdUsers.Add(credential);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    Warn(command, warnings, $"User '{user.Name}' could not be created: {ex.Message}");
                    continue;
                }

                foreach (var db in user.GrantDatabases.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        await mediator.Send(new GrantInnerUserAccessCommand(instance.Id, user.Name, db), cancellationToken);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        Warn(command, warnings, $"Granting '{db}' to user '{user.Name}' failed: {ex.Message}");
                    }
                }
            }

            // Always include the default DB in the returned list.
            createdDatabases.Insert(0, instance.DatabaseName);

            // Provisioning always produces a managed container, so ContainerName is non-null here.
            await activity.LogAsync("provision.complete", instance.ContainerName!, instance.Id, instance.Engine, $"databases={createdDatabases.Count}, users={createdUsers.Count}, warnings={warnings.Count}", cancellationToken);

            return new ProvisionResultDto
            {
                Instance = instance,
                Databases = createdDatabases,
                Users = createdUsers,
                Warnings = warnings,
            };
        }

        private static void Warn(ProvisionDatabaseCommand command, List<string> warnings, string message)
        {
            warnings.Add(message);
            command.Progress?.Report($"Warning: {message}");
        }

        /// <summary>
        /// Checks the request against everything knowable before a container exists: display
        /// name, engine, default/extra database names, user names, passwords and grants. Throws
        /// <see cref="ArgumentException"/> with a user-facing message. Returns the trimmed display
        /// name.
        /// </summary>
        public static string ValidateRequest(ProvisionRequestDto req)
        {
            var displayName = DisplayNameValidator.Normalize(req.DisplayName);
            var engine = req.Engine?.Trim() ?? "";

            // Throws ArgumentException for an unsupported engine.
            var spec = CreateDatabaseCommandHandler.ResolveEngineSpec(engine, req.Version ?? "");

            if (!string.IsNullOrEmpty(req.Password))
                SafePasswordGuard.Require(req.Password);

            var defaultDatabase = spec.FixedDatabaseName || string.IsNullOrWhiteSpace(req.DefaultDatabaseName)
                ? spec.DefaultDatabase
                : req.DefaultDatabaseName;
            if (!spec.FixedDatabaseName && !string.IsNullOrWhiteSpace(req.DefaultDatabaseName))
                ValidateDatabaseName(engine, req.DefaultDatabaseName);

            var extras = (req.Databases ?? [])
                .Where(d => !string.Equals(d, defaultDatabase, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (extras.Count > 0 && NoExtraDatabases.Contains(engine))
                throw new ArgumentException($"Engine '{engine}' does not support additional databases.", nameof(req));
            foreach (var name in extras)
                ValidateDatabaseName(engine, name);

            var users = req.Users ?? [];
            if (users.Count > 0 && NoUsers.Contains(engine))
                throw new ArgumentException($"Engine '{engine}' does not support creating users at provision time.", nameof(req));

            var duplicateUser = users.GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicateUser is not null)
                throw new ArgumentException($"User '{duplicateUser.Key}' is listed more than once.", nameof(req));

            foreach (var user in users)
            {
                InnerDatabaseNameValidator.RequireUserForEngine(engine, user.Name);
                if (!string.IsNullOrEmpty(user.Password))
                    SafePasswordGuard.Require(user.Password);
            }

            // Grants may reference the default (including the engine default when the request
            // left it blank) or any of the requested extras.
            var available = new HashSet<string>(extras.Append(defaultDatabase), StringComparer.OrdinalIgnoreCase);
            ValidateUserGrants(users, available);

            return displayName;
        }

        private static void ValidateDatabaseName(string engine, string name)
        {
            if (BucketEngines.Contains(engine))
                BucketNameValidator.Require(name);
            else
                InnerDatabaseNameValidator.RequireDatabaseForEngine(engine, name);
        }

        private static void ValidateUserGrants(IReadOnlyList<ProvisionUserSpec> users, HashSet<string> availableDatabases)
        {
            foreach (var user in users)
            {
                foreach (var db in user.GrantDatabases ?? [])
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

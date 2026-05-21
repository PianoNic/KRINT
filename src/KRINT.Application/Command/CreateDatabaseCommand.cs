using Docker.DotNet.Models;
using Mediator;
using KRINT.Application.Dtos;
using KRINT.Domain;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command
{
    public record CreateDatabaseCommand(string Engine, string Version) : ICommand<ProvisionedDatabaseDto>;

    public class CreateDatabaseCommandHandler : ICommandHandler<CreateDatabaseCommand, ProvisionedDatabaseDto>
    {
        private const string Host = "localhost";

        private readonly IDockerService _docker;
        private readonly ISecretGeneratorService _secretGenerator;
        private readonly ISecretsVaultService _vault;
        private readonly KrintDbContext _db;

        public CreateDatabaseCommandHandler(
            IDockerService docker,
            ISecretGeneratorService secretGenerator,
            ISecretsVaultService vault,
            KrintDbContext db)
        {
            _docker = docker;
            _secretGenerator = secretGenerator;
            _vault = vault;
            _db = db;
        }

        public async ValueTask<ProvisionedDatabaseDto> Handle(CreateDatabaseCommand command, CancellationToken cancellationToken)
        {
            var spec = ResolveEngineSpec(command.Engine);

            var instanceId = Guid.NewGuid();
            var instanceIdShort = instanceId.ToString("N")[..8];
            var containerName = $"krint-{spec.ShortName}-{instanceIdShort}";
            var volumeName = $"{containerName}-data";

            var password = _secretGenerator.Generate();

            await _docker.PullImageAsync(spec.Image, command.Version, cancellationToken);

            var createParams = new CreateContainerParameters
            {
                Image = $"{spec.Image}:{command.Version}",
                Name = containerName,
                Env = BuildEnv(command.Engine, password),
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    [$"{spec.InternalPort}/tcp"] = default,
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        [$"{spec.InternalPort}/tcp"] = new List<PortBinding> { new() { HostPort = string.Empty } },
                    },
                    Binds = new List<string> { $"{volumeName}:{spec.DataPath}" },
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                },
                Labels = new Dictionary<string, string>
                {
                    ["krint.managed"] = "true",
                    ["krint.engine"] = command.Engine,
                    ["krint.instance-id"] = instanceId.ToString(),
                },
            };

            var createResult = await _docker.CreateContainerAsync(createParams, cancellationToken);
            await _docker.StartContainerAsync(createResult.ID, cancellationToken);

            var inspect = await _docker.InspectContainerAsync(createResult.ID, cancellationToken);
            var hostPort = ResolveAssignedHostPort(inspect, spec.InternalPort);

            await _vault.StoreAsync($"db.{containerName}.password", password, cancellationToken);

            var instance = new DatabaseInstance
            {
                Id = instanceId,
                Engine = command.Engine,
                Version = command.Version,
                ContainerName = containerName,
                ContainerId = createResult.ID,
                Host = Host,
                Port = hostPort,
                Username = spec.DefaultUsername,
                DatabaseName = spec.DefaultDatabase,
            };
            _db.DatabaseInstances.Add(instance);
            await _db.SaveChangesAsync(cancellationToken);

            var connectionString = BuildConnectionString(command.Engine, instance.Host, hostPort, instance.Username, password, instance.DatabaseName);

            return new ProvisionedDatabaseDto
            {
                Id = instance.Id,
                Engine = instance.Engine,
                Version = instance.Version,
                ContainerName = instance.ContainerName,
                Host = instance.Host,
                Port = instance.Port,
                Username = instance.Username,
                DatabaseName = instance.DatabaseName,
                Password = password,
                ConnectionString = connectionString,
                CreatedAt = instance.CreatedAt,
            };
        }

        private record EngineSpec(
            string Image,
            string ShortName,
            int InternalPort,
            string DefaultUsername,
            string DefaultDatabase,
            string DataPath);

        private static EngineSpec ResolveEngineSpec(string engine)
        {
            switch (engine)
            {
                case "postgres":
                    return new EngineSpec("postgres", "pg", 5432, "postgres", "postgres", "/var/lib/postgresql/data");
                case "mysql":
                    return new EngineSpec("mysql", "mysql", 3306, "root", "mysql", "/var/lib/mysql");
                case "mongo":
                    return new EngineSpec("mongo", "mongo", 27017, "admin", "admin", "/data/db");
                default:
                    throw new ArgumentException($"Unsupported engine '{engine}'.", nameof(engine));
            }
        }

        private static List<string> BuildEnv(string engine, string password)
        {
            switch (engine)
            {
                case "postgres":
                    return new List<string> { $"POSTGRES_PASSWORD={password}" };
                case "mysql":
                    return new List<string> { $"MYSQL_ROOT_PASSWORD={password}" };
                case "mongo":
                    return new List<string>
                    {
                        "MONGO_INITDB_ROOT_USERNAME=admin",
                        $"MONGO_INITDB_ROOT_PASSWORD={password}",
                    };
                default:
                    throw new ArgumentException($"Unsupported engine '{engine}'.", nameof(engine));
            }
        }

        private static string BuildConnectionString(string engine, string host, int port, string username, string password, string database)
        {
            switch (engine)
            {
                case "postgres":
                    return $"postgres://{username}:{password}@{host}:{port}/{database}";
                case "mysql":
                    return $"mysql://{username}:{password}@{host}:{port}/{database}";
                case "mongo":
                    return $"mongodb://{username}:{password}@{host}:{port}/{database}?authSource=admin";
                default:
                    throw new ArgumentException($"Unsupported engine '{engine}'.", nameof(engine));
            }
        }

        private static int ResolveAssignedHostPort(ContainerInspectResponse inspect, int internalPort)
        {
            var key = $"{internalPort}/tcp";
            if (inspect.NetworkSettings?.Ports is null || !inspect.NetworkSettings.Ports.TryGetValue(key, out var bindings) || bindings is null || bindings.Count == 0)
            {
                throw new InvalidOperationException($"Container has no host binding for {key}.");
            }

            var hostPort = bindings.First().HostPort;
            if (!int.TryParse(hostPort, out var port))
            {
                throw new InvalidOperationException($"Container reports non-numeric host port '{hostPort}' for {key}.");
            }

            return port;
        }
    }
}

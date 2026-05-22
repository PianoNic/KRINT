using System.Net;
using System.Net.Sockets;
using Docker.DotNet.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KRINT.Application.Dtos;
using KRINT.Application.Options;
using KRINT.Domain;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Command
{
    public record CreateDatabaseCommand(string Engine, string Version, string? DatabaseName = null) : ICommand<ProvisionedDatabaseDto>;

    public class CreateDatabaseCommandHandler : ICommandHandler<CreateDatabaseCommand, ProvisionedDatabaseDto>
    {
        private const string Host = "localhost";

        private readonly IDockerService _docker;
        private readonly ISecretGeneratorService _secretGenerator;
        private readonly ISecretsVaultService _vault;
        private readonly KrintDbContext _db;
        private readonly KrintOptions _options;
        private readonly IActivityLogger _activity;
        private readonly IInnerDatabaseServiceResolver _innerDbs;

        public CreateDatabaseCommandHandler(
            IDockerService docker,
            ISecretGeneratorService secretGenerator,
            ISecretsVaultService vault,
            KrintDbContext db,
            IOptions<KrintOptions> options,
            IActivityLogger activity,
            IInnerDatabaseServiceResolver innerDbs)
        {
            _docker = docker;
            _secretGenerator = secretGenerator;
            _vault = vault;
            _db = db;
            _options = options.Value;
            _activity = activity;
            _innerDbs = innerDbs;
        }

        public async ValueTask<ProvisionedDatabaseDto> Handle(CreateDatabaseCommand command, CancellationToken cancellationToken)
        {
            var spec = ResolveEngineSpec(command.Engine, command.Version);

            var databaseName = ResolveDatabaseName(command.Engine, command.DatabaseName, spec.DefaultDatabase);

            var instanceId = Guid.NewGuid();
            var instanceIdShort = instanceId.ToString("N")[..8];
            var containerName = $"krint-{spec.ShortName}-{instanceIdShort}";
            var volumeName = $"{containerName}-data";

            var password = _secretGenerator.Generate();

            await _docker.PullImageAsync(spec.Image, command.Version, cancellationToken);

            var hostPort = await AllocateHostPortAsync(command.Engine, cancellationToken);

            var createParams = new CreateContainerParameters
            {
                Image = $"{spec.Image}:{command.Version}",
                Name = containerName,
                Env = BuildEnv(command.Engine, password, databaseName, spec.DefaultDatabase),
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    [$"{spec.InternalPort}/tcp"] = default,
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        [$"{spec.InternalPort}/tcp"] = new List<PortBinding> { new() { HostPort = hostPort.ToString() } },
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

            await _vault.StoreAsync(ConnectionStringBuilder.VaultKeyFor(containerName), password, cancellationToken);

            // Wait for the engine inside the container to accept connections. Postgres especially
            // needs a few seconds to initialise PGDATA on first start; without this poll the next
            // op races into a still-booting server and Npgsql sees EOF on the SSL probe.
            var readinessTarget = new InnerDatabaseTarget(
                command.Engine, Host, hostPort,
                spec.DefaultUsername, password, databaseName);
            await WaitForReadyAsync(readinessTarget, cancellationToken);

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
                DatabaseName = databaseName,
            };
            _db.DatabaseInstances.Add(instance);
            await _db.SaveChangesAsync(cancellationToken);

            await _activity.LogAsync(
                "instance.create",
                containerName,
                instance.Id,
                command.Engine,
                $"version={command.Version}, port={hostPort}",
                cancellationToken);

            var connectionString = ConnectionStringBuilder.Build(command.Engine, instance.Host, hostPort, instance.Username, password, instance.DatabaseName);

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

        private static EngineSpec ResolveEngineSpec(string engine, string version)
        {
            switch (engine)
            {
                case "postgres":
                    // pg 18+ stores data in /var/lib/postgresql/<major>/docker — mount the parent.
                    // pg <=17 uses PGDATA=/var/lib/postgresql/data — mount that directly.
                    var pgDataPath = TryGetMajorVersion(version) is { } major && major >= 18
                        ? "/var/lib/postgresql"
                        : "/var/lib/postgresql/data";
                    return new EngineSpec("postgres", "pg", 5432, "postgres", "postgres", pgDataPath);
                case "mysql":
                    return new EngineSpec("mysql", "mysql", 3306, "root", "mysql", "/var/lib/mysql");
                case "mariadb":
                    return new EngineSpec("mariadb", "maria", 3306, "root", "mariadb", "/var/lib/mysql");
                case "mongo":
                    return new EngineSpec("mongo", "mongo", 27017, "admin", "admin", "/data/db");
                default:
                    throw new ArgumentException($"Unsupported engine '{engine}'.", nameof(engine));
            }
        }

        private static int? TryGetMajorVersion(string version)
        {
            var head = version.Split('.', '-')[0];
            return int.TryParse(head, out var major) ? major : null;
        }

        private static string ResolveDatabaseName(string engine, string? requested, string defaultName)
        {
            if (string.IsNullOrWhiteSpace(requested)) return defaultName;
            KRINT.Infrastructure.Services.InnerDatabaseNameValidator.Require(requested);
            return requested;
        }

        private static List<string> BuildEnv(string engine, string password, string databaseName, string defaultDatabaseName)
        {
            switch (engine)
            {
                case "postgres":
                    var pgEnv = new List<string> { $"POSTGRES_PASSWORD={password}" };
                    if (!string.Equals(databaseName, defaultDatabaseName, StringComparison.Ordinal))
                        pgEnv.Add($"POSTGRES_DB={databaseName}");
                    return pgEnv;
                case "mysql":
                    var mysqlEnv = new List<string> { $"MYSQL_ROOT_PASSWORD={password}" };
                    if (!string.Equals(databaseName, defaultDatabaseName, StringComparison.Ordinal))
                        mysqlEnv.Add($"MYSQL_DATABASE={databaseName}");
                    return mysqlEnv;
                case "mariadb":
                    // The mariadb image accepts both MARIADB_* and MYSQL_* env vars (the latter for
                    // drop-in compatibility); we send the modern MARIADB_* names.
                    var mariaEnv = new List<string> { $"MARIADB_ROOT_PASSWORD={password}" };
                    if (!string.Equals(databaseName, defaultDatabaseName, StringComparison.Ordinal))
                        mariaEnv.Add($"MARIADB_DATABASE={databaseName}");
                    return mariaEnv;
                case "mongo":
                    // Mongo creates databases lazily; the env vars only seed the root user/auth db.
                    // The chosen databaseName is recorded in the connection string but not pre-created.
                    return new List<string>
                    {
                        "MONGO_INITDB_ROOT_USERNAME=admin",
                        $"MONGO_INITDB_ROOT_PASSWORD={password}",
                    };
                default:
                    throw new ArgumentException($"Unsupported engine '{engine}'.", nameof(engine));
            }
        }

        private async Task WaitForReadyAsync(InnerDatabaseTarget target, CancellationToken cancellationToken)
        {
            var inner = _innerDbs.Resolve(target.Engine);
            var deadline = DateTime.UtcNow.AddSeconds(45);
            var delayMs = 500;
            Exception? last = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await inner.ListAsync(target, cancellationToken);
                    return;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    last = ex;
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs = Math.Min(delayMs * 2, 3000);
                }
            }
            throw new InvalidOperationException(
                $"{target.Engine} container did not become ready within 45s.", last);
        }

        private async Task<int> AllocateHostPortAsync(string engine, CancellationToken cancellationToken)
        {
            var range = _options.GetPortRange(engine);

            var used = await _db.DatabaseInstances
                .Where(d => d.Engine == engine && d.Port >= range.Start && d.Port <= range.End)
                .Select(d => d.Port)
                .ToHashSetAsync(cancellationToken);

            for (var port = range.Start; port <= range.End; port++)
            {
                if (used.Contains(port)) continue;
                if (!IsPortFree(port)) continue;
                return port;
            }

            throw new InvalidOperationException($"No free host port in range {range.Start}-{range.End} for engine '{engine}'.");
        }

        private static bool IsPortFree(int port)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}

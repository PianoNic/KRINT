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
    public record CreateDatabaseCommand(
        string Engine,
        string Version,
        string DisplayName,
        string? DatabaseName = null,
        IReadOnlyList<string>? Plugins = null) : ICommand<ProvisionedDatabaseDto>;

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

            // Resolve selected plugins. DockerImageSwap replaces the image; EnvFlag appends env;
            // PgExtension / ContainerExec are applied later after readiness.
            var selectedPlugins = ResolvePlugins(command.Plugins);
            var imageOverride = selectedPlugins.FirstOrDefault(p => p.InstallMode == Dtos.PluginInstallMode.DockerImageSwap)?.Payload;
            var extraEnv = selectedPlugins
                .Where(p => p.InstallMode == Dtos.PluginInstallMode.EnvFlag)
                .Select(p => p.Payload)
                .ToList();

            var instanceId = Guid.NewGuid();
            var instanceIdShort = instanceId.ToString("N")[..8];
            var containerName = $"krint-{spec.ShortName}-{instanceIdShort}";
            var volumeName = $"{containerName}-data";

            var password = _secretGenerator.Generate();

            var imageName = imageOverride ?? spec.Image;
            // pgvector/pgvector publishes tags as pg<major> (pg15..pg18), not as the upstream
            // Postgres tag (e.g. 18.4). When the swap is active, translate the picked Postgres
            // version to the matching pgvector tag - otherwise the pull 404s.
            var imageTag = imageOverride == "pgvector/pgvector"
                ? PgVectorTagFor(command.Version)
                : command.Version;
            await _docker.PullImageAsync(imageName, imageTag, cancellationToken);

            var hostPort = await AllocateHostPortAsync(command.Engine, cancellationToken);

            var env = BuildEnv(command.Engine, password, databaseName, spec.DefaultDatabase);
            env.AddRange(extraEnv);

            var createParams = new CreateContainerParameters
            {
                Image = $"{imageName}:{imageTag}",
                Name = containerName,
                Env = env,
                Cmd = spec.CmdFactory?.Invoke(password),
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

            // From here on, any exception means we own a half-provisioned container that the
            // caller never sees. Track success and clean up in a finally — otherwise stale
            // containers hold ports and break the next provision attempt.
            var provisionedOk = false;
            try
            {
                await _docker.StartContainerAsync(createResult.ID, cancellationToken);
                await _vault.StoreAsync(ConnectionStringBuilder.VaultKeyFor(containerName), password, cancellationToken);

                // Wait for the engine inside the container to accept connections.
                var readinessTarget = new InnerDatabaseTarget(
                    command.Engine, Host, hostPort,
                    spec.DefaultUsername, password, databaseName);
                await WaitForReadyAsync(readinessTarget, cancellationToken);

                // pgvector engine entry: enable the extension once the container accepts connections.
                if (string.Equals(command.Engine, "pgvector", StringComparison.OrdinalIgnoreCase))
                {
                    await RunPgInitSqlAsync(readinessTarget, "CREATE EXTENSION IF NOT EXISTS vector", cancellationToken);
                }

                // Apply post-readiness plugin steps. Order doesn't matter - each is idempotent.
                foreach (var plugin in selectedPlugins)
                {
                    switch (plugin.InstallMode)
                    {
                        case Dtos.PluginInstallMode.PgExtension:
                            await RunPgInitSqlAsync(readinessTarget, $"CREATE EXTENSION IF NOT EXISTS {plugin.Payload}", cancellationToken);
                            break;
                        case Dtos.PluginInstallMode.ContainerExec:
                            await _docker.ExecCaptureAsync(createResult.ID, new[] { "sh", "-c", plugin.Payload }, cancellationToken);
                            break;
                        // DockerImageSwap + EnvFlag were already applied above.
                    }
                }

                var instance = new DatabaseInstance
                {
                    Id = instanceId,
                    Engine = command.Engine,
                    Version = command.Version,
                    DisplayName = command.DisplayName,
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

                provisionedOk = true;
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
            finally
            {
                if (!provisionedOk)
                {
                    // Tear down the container + volume + vault entry so a retry isn't blocked
                    // by a half-provisioned state holding the host port.
                    try { await _docker.RemoveContainerAsync(createResult.ID, force: true, CancellationToken.None); } catch { }
                    try { await _vault.DeleteAsync(ConnectionStringBuilder.VaultKeyFor(containerName), CancellationToken.None); } catch { }
                }
            }
        }

        private record EngineSpec(
            string Image,
            string ShortName,
            int InternalPort,
            string DefaultUsername,
            string DefaultDatabase,
            string DataPath,
            // Optional CMD override. The Redis image's default ENTRYPOINT doesn't read REDIS_PASSWORD,
            // so we explicitly pass `--requirepass <pw>` as the container command.
            Func<string, IList<string>?>? CmdFactory = null);

        private static EngineSpec ResolveEngineSpec(string engine, string version)
        {
            switch (engine)
            {
                case "postgres":
                    // pg 18+ stores data in /var/lib/postgresql/<major>/docker - mount the parent.
                    // pg <=17 uses PGDATA=/var/lib/postgresql/data - mount that directly.
                    var pgDataPath = TryGetMajorVersion(version) is { } major && major >= 18
                        ? "/var/lib/postgresql"
                        : "/var/lib/postgresql/data";
                    return new EngineSpec("postgres", "pg", 5432, "postgres", "postgres", pgDataPath);
                case "timescaledb":
                    // timescale/timescaledb tags use Postgres <=17 layout (PGDATA=/var/lib/postgresql/data).
                    return new EngineSpec("timescale/timescaledb", "tsdb", 5432, "postgres", "postgres", "/var/lib/postgresql/data");
                case "mysql":
                    return new EngineSpec("mysql", "mysql", 3306, "root", "mysql", "/var/lib/mysql");
                case "mariadb":
                    return new EngineSpec("mariadb", "maria", 3306, "root", "mariadb", "/var/lib/mysql");
                case "mongo":
                    return new EngineSpec("mongo", "mongo", 27017, "admin", "admin", "/data/db");
                case "redis":
                    // Redis has no built-in user concept at provision time - auth is via requirepass
                    // (sent as the password). Username is left as "default" to match Redis 6+ ACL conventions.
                    return new EngineSpec("redis", "redis", 6379, "default", "0", "/data",
                        CmdFactory: pwd => new[] { "redis-server", "--requirepass", pwd, "--appendonly", "yes" });
                case "cockroachdb":
                    // `start-single-node --insecure` skips TLS/auth so the generated password is ignored
                    // at the engine layer. The vault still stores the password for parity with other
                    // engines, but the Postgres client connects as root with no password.
                    return new EngineSpec("cockroachdb/cockroach", "crdb", 26257, "root", "defaultdb", "/cockroach/cockroach-data",
                        CmdFactory: _ => new[] { "start-single-node", "--insecure", "--accept-sql-without-tls" });
                case "clickhouse":
                    // ClickHouse image takes CLICKHOUSE_USER/CLICKHOUSE_PASSWORD/CLICKHOUSE_DB env vars.
                    // We publish the HTTP port (8123) - that's what ClickHouse.Client speaks.
                    return new EngineSpec("clickhouse/clickhouse-server", "ch", 8123, "default", "default", "/var/lib/clickhouse");
                case "cassandra":
                    // Cassandra image doesn't take a password env. We provision with auth disabled.
                    return new EngineSpec("cassandra", "cass", 9042, "cassandra", "system", "/var/lib/cassandra");
                case "couchdb":
                    // COUCHDB_USER / COUCHDB_PASSWORD seed the admin account on first boot.
                    return new EngineSpec("couchdb", "couch", 5984, "admin", "default", "/opt/couchdb/data");
                case "elasticsearch":
                    // ELASTIC_PASSWORD is read from env; xpack.security.enabled is on by default in 8.x.
                    return new EngineSpec("elasticsearch", "es", 9200, "elastic", "_cluster", "/usr/share/elasticsearch/data");
                case "pgvector":
                    // pgvector/pgvector tags use Postgres <=17 layout. CREATE EXTENSION vector runs
                    // after the container is ready (see InitSqlFactory below).
                    return new EngineSpec("pgvector/pgvector", "pgvec", 5432, "postgres", "postgres", "/var/lib/postgresql/data");
                case "neo4j":
                    // NEO4J_AUTH is read as "user/password" - we pass it via env. Default DB is "neo4j".
                    return new EngineSpec("neo4j", "neo4j", 7687, "neo4j", "neo4j", "/data");
                case "qdrant":
                    // QDRANT__SERVICE__API_KEY is sent in the api-key header by our HTTP client.
                    return new EngineSpec("qdrant/qdrant", "qdrant", 6333, "default", "_cluster", "/qdrant/storage");
                case "valkey":
                    // Drop-in Redis fork - same CMD shape.
                    return new EngineSpec("valkey/valkey", "valkey", 6379, "default", "0", "/data",
                        CmdFactory: pwd => new[] { "valkey-server", "--requirepass", pwd, "--appendonly", "yes" });
                case "mssql":
                    // SA password must be ≥8 chars, mixed case, digit, special. SecretGenerator already does this.
                    return new EngineSpec("mcr.microsoft.com/mssql/server", "mssql", 1433, "sa", "master", "/var/opt/mssql");
                default:
                    throw new ArgumentException($"Unsupported engine '{engine}'.", nameof(engine));
            }
        }

        private static int? TryGetMajorVersion(string version)
        {
            var head = version.Split('.', '-')[0];
            return int.TryParse(head, out var major) ? major : null;
        }

        // pgvector/pgvector tags are pg<major>. The user picks an upstream Postgres version
        // (e.g. "18.4" or "18"); we extract the major and produce "pg18". If the user already
        // typed something pg-shaped (e.g. "pg17") we pass it through unchanged.
        private static string PgVectorTagFor(string postgresVersion)
        {
            if (postgresVersion.StartsWith("pg", StringComparison.OrdinalIgnoreCase))
                return postgresVersion;
            var major = TryGetMajorVersion(postgresVersion);
            return major is null ? postgresVersion : $"pg{major}";
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
                case "timescaledb":
                    // TimescaleDB image reuses stock Postgres env vars.
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
                case "redis":
                    // The official redis image reads REDIS_PASSWORD when the command isn't overridden.
                    // We add `--requirepass <pw>` as the container command so it definitely applies.
                    return new List<string> { $"REDIS_PASSWORD={password}" };
                case "cockroachdb":
                    // --insecure mode doesn't read a password from env. Nothing to set.
                    return new List<string>();
                case "clickhouse":
                    var chEnv = new List<string>
                    {
                        $"CLICKHOUSE_USER=default",
                        $"CLICKHOUSE_PASSWORD={password}",
                        // The image lets non-root users in by default - turn that off.
                        $"CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT=1",
                    };
                    if (!string.Equals(databaseName, defaultDatabaseName, StringComparison.Ordinal))
                        chEnv.Add($"CLICKHOUSE_DB={databaseName}");
                    return chEnv;
                case "cassandra":
                    // Image runs with auth disabled by default; no env vars needed.
                    return new List<string>();
                case "couchdb":
                    return new List<string>
                    {
                        "COUCHDB_USER=admin",
                        $"COUCHDB_PASSWORD={password}",
                    };
                case "elasticsearch":
                    return new List<string>
                    {
                        $"ELASTIC_PASSWORD={password}",
                        // Single-node cluster, no TLS for the dev local case.
                        "discovery.type=single-node",
                        "xpack.security.enabled=true",
                        "xpack.security.http.ssl.enabled=false",
                        "ES_JAVA_OPTS=-Xms512m -Xmx512m",
                    };
                case "pgvector":
                    var pgvecEnv = new List<string> { $"POSTGRES_PASSWORD={password}" };
                    if (!string.Equals(databaseName, defaultDatabaseName, StringComparison.Ordinal))
                        pgvecEnv.Add($"POSTGRES_DB={databaseName}");
                    return pgvecEnv;
                case "neo4j":
                    return new List<string>
                    {
                        $"NEO4J_AUTH=neo4j/{password}",
                        // Accept the Community licence non-interactively.
                        "NEO4J_ACCEPT_LICENSE_AGREEMENT=yes",
                    };
                case "qdrant":
                    return new List<string> { $"QDRANT__SERVICE__API_KEY={password}" };
                case "valkey":
                    return new List<string>();   // Auth comes via --requirepass on the command line.
                case "mssql":
                    return new List<string>
                    {
                        "ACCEPT_EULA=Y",
                        $"MSSQL_SA_PASSWORD={password}",
                        "MSSQL_PID=Developer",
                    };
                default:
                    throw new ArgumentException($"Unsupported engine '{engine}'.", nameof(engine));
            }
        }

        private static IReadOnlyList<Dtos.EnginePluginDto> ResolvePlugins(IReadOnlyList<string>? keys)
        {
            if (keys is null || keys.Count == 0) return Array.Empty<Dtos.EnginePluginDto>();
            var catalog = Queries.GetSupportedDatabasesQueryHandler.AllPluginsByKey;
            var result = new List<Dtos.EnginePluginDto>();
            foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (catalog.TryGetValue(key, out var plugin)) result.Add(plugin);
                // Unknown keys are silently dropped - the FE only exposes catalog plugins.
            }
            return result;
        }

        private static async Task RunPgInitSqlAsync(InnerDatabaseTarget target, string sql, CancellationToken cancellationToken)
        {
            var csb = new Npgsql.NpgsqlConnectionStringBuilder
            {
                Host = target.Host,
                Port = target.Port,
                Username = target.Username,
                Password = target.Password,
                Database = target.DefaultDatabase,
                Pooling = false,
                Timeout = 5,
            };
            await using var conn = new Npgsql.NpgsqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task WaitForReadyAsync(InnerDatabaseTarget target, CancellationToken cancellationToken)
        {
            var inner = _innerDbs.Resolve(target.Engine);
            // Cold-boot envelope per engine. JVM-heavy ones (Cassandra, Elasticsearch, Neo4j)
            // routinely need >60s on first start; SQL engines + cache stores come up in a
            // handful of seconds.
            var ceilingSeconds = target.Engine switch
            {
                "cassandra" or "elasticsearch" or "neo4j" => 180,
                _ => 60,
            };
            var deadline = DateTime.UtcNow.AddSeconds(ceilingSeconds);
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
                $"{target.Engine} container did not become ready within {ceilingSeconds}s.", last);
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

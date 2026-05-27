using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos.DatabaseInstance;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.Database
{
    public record DiscoverContainersQuery : IQuery<IReadOnlyList<DiscoveredContainerDto>>;

    /// <summary>
    /// Walks the host's Docker containers, picks ones whose image matches a supported engine,
    /// parses creds + port out of the inspect response, and returns them as candidates for the
    /// Register flow. Skips:
    ///   - anything labelled krint.managed=true (KRINT provisioned it; already in the list)
    ///   - anything whose container id matches a row in DatabaseInstances (registered already
    ///     as an external instance from an earlier discover round)
    /// </summary>
    public class DiscoverContainersQueryHandler(KrintDbContext db, IDockerService docker)
        : IQueryHandler<DiscoverContainersQuery, IReadOnlyList<DiscoveredContainerDto>>
    {
        public async ValueTask<IReadOnlyList<DiscoveredContainerDto>> Handle(DiscoverContainersQuery query, CancellationToken cancellationToken)
        {
            var containers = await docker.ListContainersAsync(all: true, cancellationToken);

            var trackedIds = await db.DatabaseInstances
                .Where(d => d.ContainerId != null)
                .Select(d => d.ContainerId!)
                .ToHashSetAsync(cancellationToken);

            var results = new List<DiscoveredContainerDto>();
            foreach (var c in containers)
            {
                // KRINT-managed containers are already in the instances list - don't re-offer.
                if (c.Labels is not null && c.Labels.TryGetValue("krint.managed", out var managed) && managed == "true")
                    continue;
                if (trackedIds.Contains(c.ID)) continue;

                var (image, tag) = SplitImage(c.Image);
                var engine = ImageToEngine(image);
                if (engine is null) continue;

                // Need the inspect to pull env vars (creds + db name). ListContainers doesn't
                // include env. One inspect per candidate; volumes are typically small.
                var inspect = await docker.InspectContainerAsync(c.ID, cancellationToken);
                var env = ParseEnv(inspect.Config?.Env);
                var spec = EngineDefaults(engine);

                var port = (int)(c.Ports?.FirstOrDefault(p => p.PrivatePort == spec.InternalPort && p.PublicPort != 0)?.PublicPort
                                 ?? c.Ports?.FirstOrDefault(p => p.PublicPort != 0)?.PublicPort
                                 ?? 0);

                // Compose labels are present iff the container was launched via `docker compose`.
                // Used downstream by the migration flow to (a) gate the "Migrate into KRINT"
                // button and (b) tell the user which compose file and service to remove after
                // the migration succeeds. config_files can be a comma-separated list; we keep
                // the first entry, which is the primary compose file.
                string? composeProject = null;
                string? composeService = null;
                string? composeFilePath = null;
                if (c.Labels is not null)
                {
                    c.Labels.TryGetValue("com.docker.compose.project", out composeProject);
                    c.Labels.TryGetValue("com.docker.compose.service", out composeService);
                    if (c.Labels.TryGetValue("com.docker.compose.project.config_files", out var cfg) && !string.IsNullOrWhiteSpace(cfg))
                    {
                        var first = cfg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                        composeFilePath = string.IsNullOrEmpty(first) ? null : first;
                    }
                }

                results.Add(new DiscoveredContainerDto
                {
                    ContainerId = c.ID,
                    ContainerName = (c.Names?.FirstOrDefault() ?? c.ID).TrimStart('/'),
                    Engine = engine,
                    Image = c.Image,
                    Version = tag,
                    Host = "localhost",
                    Port = port,
                    Username = spec.Username,
                    Password = ExtractPassword(engine, env),
                    DatabaseName = ExtractDatabase(engine, env) ?? spec.DefaultDatabase,
                    State = c.State ?? "unknown",
                    ComposeProject = string.IsNullOrEmpty(composeProject) ? null : composeProject,
                    ComposeService = string.IsNullOrEmpty(composeService) ? null : composeService,
                    ComposeFilePath = composeFilePath,
                });
            }

            return results
                .OrderByDescending(r => string.Equals(r.State, "running", StringComparison.OrdinalIgnoreCase))
                .ThenBy(r => r.ContainerName)
                .ToList();
        }

        private static (string Image, string Tag) SplitImage(string image)
        {
            if (string.IsNullOrEmpty(image)) return ("", "");
            // Strip @sha256:... digest if present, then split on the last ':' (registries can
            // contain ':' for ports — but the *tag* separator is always the last one after the
            // final '/').
            var atIdx = image.IndexOf('@');
            var bare = atIdx >= 0 ? image[..atIdx] : image;
            var slashIdx = bare.LastIndexOf('/');
            var afterSlash = slashIdx >= 0 ? bare[(slashIdx + 1)..] : bare;
            var colonIdx = afterSlash.LastIndexOf(':');
            if (colonIdx < 0) return (bare, "latest");
            var tag = afterSlash[(colonIdx + 1)..];
            var name = slashIdx >= 0 ? bare[..(slashIdx + 1)] + afterSlash[..colonIdx] : bare[..colonIdx];
            return (name, tag);
        }

        // Maps a Docker image name to one of KRINT's supported engine keys. Unknown images
        // return null so the caller can skip them.
        private static string? ImageToEngine(string image)
        {
            var i = image.ToLowerInvariant();
            return i switch
            {
                "postgres" => "postgres",
                "mysql" => "mysql",
                "mariadb" => "mariadb",
                "mongo" or "mongodb/mongodb-community-server" => "mongo",
                "redis" => "redis",
                "valkey/valkey" => "valkey",
                "cockroachdb/cockroach" => "cockroachdb",
                "clickhouse/clickhouse-server" => "clickhouse",
                "cassandra" => "cassandra",
                "couchdb" => "couchdb",
                "elasticsearch" or "docker.elastic.co/elasticsearch/elasticsearch" => "elasticsearch",
                "pgvector/pgvector" => "pgvector",
                "neo4j" => "neo4j",
                "qdrant/qdrant" => "qdrant",
                "timescale/timescaledb" or "timescale/timescaledb-ha" => "timescaledb",
                "mcr.microsoft.com/mssql/server" => "mssql",
                _ => null,
            };
        }

        // Defaults used when env vars don't reveal a value (e.g. database name fallback).
        // Mirrors CreateDatabaseCommandHandler.ResolveEngineSpec but only the fields discovery
        // needs - kept here to avoid pulling that internal type out of its current namespace.
        private static (string Username, string DefaultDatabase, int InternalPort) EngineDefaults(string engine) => engine switch
        {
            "postgres" or "pgvector" or "timescaledb" => ("postgres", "postgres", 5432),
            "mysql" => ("root", "mysql", 3306),
            "mariadb" => ("root", "mariadb", 3306),
            "mongo" => ("admin", "admin", 27017),
            "redis" or "valkey" => ("default", "0", 6379),
            "cockroachdb" => ("root", "defaultdb", 26257),
            "clickhouse" => ("default", "default", 8123),
            "cassandra" => ("cassandra", "system", 9042),
            "couchdb" => ("admin", "default", 5984),
            "elasticsearch" => ("elastic", "_cluster", 9200),
            "neo4j" => ("neo4j", "neo4j", 7687),
            "qdrant" => ("default", "_cluster", 6333),
            "mssql" => ("sa", "master", 1433),
            _ => ("", "", 0),
        };

        private static Dictionary<string, string> ParseEnv(IList<string>? env)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (env is null) return dict;
            foreach (var line in env)
            {
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;
                dict[line[..idx]] = line[(idx + 1)..];
            }
            return dict;
        }

        private static string? ExtractPassword(string engine, Dictionary<string, string> env)
        {
            string? Try(params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (env.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v)) return v;
                }
                return null;
            }
            return engine switch
            {
                "postgres" or "pgvector" or "timescaledb" => Try("POSTGRES_PASSWORD"),
                "mysql" => Try("MYSQL_ROOT_PASSWORD", "MYSQL_PASSWORD"),
                "mariadb" => Try("MARIADB_ROOT_PASSWORD", "MYSQL_ROOT_PASSWORD"),
                "mongo" => Try("MONGO_INITDB_ROOT_PASSWORD"),
                "redis" => Try("REDIS_PASSWORD"),
                "clickhouse" => Try("CLICKHOUSE_PASSWORD"),
                "couchdb" => Try("COUCHDB_PASSWORD"),
                "elasticsearch" => Try("ELASTIC_PASSWORD"),
                "neo4j" => env.TryGetValue("NEO4J_AUTH", out var auth) && auth.Contains('/') ? auth[(auth.IndexOf('/') + 1)..] : null,
                "qdrant" => Try("QDRANT__SERVICE__API_KEY"),
                "mssql" => Try("MSSQL_SA_PASSWORD", "SA_PASSWORD"),
                _ => null,
            };
        }

        private static string? ExtractDatabase(string engine, Dictionary<string, string> env)
        {
            return engine switch
            {
                "postgres" or "pgvector" or "timescaledb" => env.TryGetValue("POSTGRES_DB", out var v) ? v : null,
                "mysql" => env.TryGetValue("MYSQL_DATABASE", out var v) ? v : null,
                "mariadb" => env.TryGetValue("MARIADB_DATABASE", out var v) ? v : (env.TryGetValue("MYSQL_DATABASE", out var v2) ? v2 : null),
                "clickhouse" => env.TryGetValue("CLICKHOUSE_DB", out var v) ? v : null,
                _ => null,
            };
        }
    }
}

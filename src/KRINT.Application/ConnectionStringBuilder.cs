using KRINT.Domain;

namespace KRINT.Application
{
    public static class ConnectionStringBuilder
    {
        public static string Build(string engine, string host, int port, string username, string password, string database)
        {
            return engine switch
            {
                "postgres" => $"postgres://{username}:{password}@{host}:{port}/{database}",
                "timescaledb" => $"postgres://{username}:{password}@{host}:{port}/{database}",
                "mysql" => $"mysql://{username}:{password}@{host}:{port}/{database}",
                "mariadb" => $"mariadb://{username}:{password}@{host}:{port}/{database}",
                "mongo" => $"mongodb://{username}:{password}@{host}:{port}/{database}?authSource=admin",
                // For Redis we surface the connection as a redis:// URL with an empty user (Redis pre-ACL clients ignore it).
                "redis" => $"redis://default:{password}@{host}:{port}/{database}",
                // CockroachDB uses the Postgres wire protocol - same URL shape.
                "cockroachdb" => $"postgres://{username}@{host}:{port}/{database}?sslmode=disable",
                // ClickHouse exposes HTTP at this port; the URL works in clickhouse-client too.
                "clickhouse" => $"http://{username}:{password}@{host}:{port}/?database={database}",
                "cassandra" => $"cassandra://{host}:{port}/{database}",
                "couchdb" => $"http://{username}:{password}@{host}:{port}/",
                "pgvector" => $"postgres://{username}:{password}@{host}:{port}/{database}",
                "neo4j" => $"bolt://{username}:{password}@{host}:{port}",
                "qdrant" => $"http://{host}:{port} (api-key: {password})",
                "valkey" => $"redis://default:{password}@{host}:{port}/{database}",
                "mssql" => $"Server={host},{port};User Id={username};Password={password};Database={database};TrustServerCertificate=true",
                "seaweedfs" => $"http://{host}:{port} (S3 access-key: {username}, secret-key: {password})",
                "azurite" => $"DefaultEndpointsProtocol=http;AccountName={username};AccountKey=<azurite-dev-key>;BlobEndpoint=http://{host}:{port}/{username};",
                _ => throw new ArgumentException($"Unsupported engine '{engine}'.", nameof(engine)),
            };
        }

        public static string VaultKeyFor(string containerName) => $"db.{containerName}.password";

        /// <summary>Picks the right vault key for either a KRINT-managed instance (keyed by
        /// container name) or an externally-registered one (keyed by instance id).</summary>
        public static string VaultKeyFor(DatabaseInstance instance) => instance.IsManaged && instance.ContainerName is not null
            ? VaultKeyFor(instance.ContainerName)
            : $"db.external-{instance.Id:N}.password";
    }
}

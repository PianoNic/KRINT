using KRINT.Domain;

namespace KRINT.Application
{
    public static class ConnectionStringBuilder
    {
        public static string Build(string engine, string host, int port, string username, string password, string database)
        {
            // URL forms: a password holding @ : / ? # or % would otherwise end the userinfo early
            // and point the URL at the wrong host or database, so user and password are escaped.
            var u = Uri.EscapeDataString(username);
            var p = Uri.EscapeDataString(password);
            return engine switch
            {
                "postgres" => $"postgres://{u}:{p}@{host}:{port}/{database}",
                "timescaledb" => $"postgres://{u}:{p}@{host}:{port}/{database}",
                "mysql" => $"mysql://{u}:{p}@{host}:{port}/{database}",
                "mariadb" => $"mariadb://{u}:{p}@{host}:{port}/{database}",
                "mongo" => $"mongodb://{u}:{p}@{host}:{port}/{database}?authSource=admin",
                // For Redis we surface the connection as a redis:// URL with an empty user (Redis pre-ACL clients ignore it).
                "redis" => $"redis://default:{p}@{host}:{port}/{database}",
                // CockroachDB uses the Postgres wire protocol - same URL shape.
                "cockroachdb" => $"postgres://{u}@{host}:{port}/{database}?sslmode=disable",
                // ClickHouse exposes HTTP at this port; the URL works in clickhouse-client too.
                "clickhouse" => $"http://{u}:{p}@{host}:{port}/?database={Uri.EscapeDataString(database)}",
                "cassandra" => $"cassandra://{host}:{port}/{database}",
                "couchdb" => $"http://{u}:{p}@{host}:{port}/",
                "pgvector" => $"postgres://{u}:{p}@{host}:{port}/{database}",
                "neo4j" => $"bolt://{u}:{p}@{host}:{port}",
                // Qdrant authenticates with an api-key header, not a URL credential; the key is the
                // instance password shown beside this string.
                "qdrant" => $"http://{host}:{port}",
                "valkey" => $"redis://default:{p}@{host}:{port}/{database}",
                "mssql" => $"Server={host},{port};User Id={username};Password={password};Database={database};TrustServerCertificate=true",
                // S3 endpoint; access key and secret are the instance username and password.
                "seaweedfs" => $"http://{host}:{port}",
                // Azurite's fixed development account and its published, well-known key.
                "azurite" => $"DefaultEndpointsProtocol=http;AccountName={username};AccountKey={AzuriteDevelopmentKey};BlobEndpoint=http://{host}:{port}/{username};",
                _ => throw new ArgumentException($"Unsupported engine '{engine}'.", nameof(engine)),
            };
        }

        /// <summary>The account key every Azurite instance accepts for devstoreaccount1. It is
        /// documented by Microsoft and identical on every emulator, so it is not a secret.</summary>
        public const string AzuriteDevelopmentKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

        public static string VaultKeyFor(string containerName) => $"db.{containerName}.password";

        /// <summary>Picks the right vault key for either a KRINT-managed instance (keyed by
        /// container name) or an externally-registered one (keyed by instance id).</summary>
        public static string VaultKeyFor(DatabaseInstance instance) => instance.IsManaged && instance.ContainerName is not null
            ? VaultKeyFor(instance.ContainerName)
            : $"db.external-{instance.Id:N}.password";
    }
}

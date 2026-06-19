using Mediator;
using KRINT.Application.Dtos.SupportedDatabase;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries.SupportedDatabase
{
    public record GetSupportedDatabasesQuery : IQuery<IReadOnlyList<SupportedDatabaseDto>>;

    public class GetSupportedDatabasesQueryHandler(IDatabaseVersionService versions)
        : IQueryHandler<GetSupportedDatabasesQuery, IReadOnlyList<SupportedDatabaseDto>>
    {
        private static readonly EngineCapabilitiesDto SqlCaps = new()
        {
            DatabaseTerm = "database",
            TableTerm = "table",
            RowTerm = "row",
            SupportsListDatabases = true,
            SupportsCreateDatabase = true,
            SupportsDropDatabase = true,
            SupportsListTables = true,
            SupportsDropTable = true,
            SupportsRowRead = true,
            SupportsRowInsert = true,
            SupportsRowEdit = true,
            SupportsRowDelete = true,
            SupportsUsers = true,
            SupportsBackup = true,
        };

        // Mongo: docs rendered as a single JSON column; insert/replace/delete keyed on _id.
        private static readonly EngineCapabilitiesDto MongoCaps = SqlCaps with
        {
            TableTerm = "collection",
            RowTerm = "document",
        };

        // CockroachDB: Postgres-wire-compatible but no `ctid` and no pg_dump. Row edit/delete work
        // via a plain WHERE on the loaded values (the match-count guard already enforces exactly-one
        // -match, so ctid isn't needed); the schema service overrides the ctid statements. Backup
        // stays disabled until a cockroach-native dump path is added.
        private static readonly EngineCapabilitiesDto CockroachCaps = SqlCaps with
        {
            SupportsBackup = false,
        };

        // ClickHouse: SQL-shaped (databases / tables / rows / users). Row edit/delete go through
        // ALTER ... UPDATE/DELETE mutations, run synchronously (mutations_sync=2) and guarded by a
        // one-row match check, with sorting-key columns locked. Backup needs clickhouse-backup or
        // BACKUP TO Disk - still out of scope for v1.
        private static readonly EngineCapabilitiesDto ClickhouseCaps = SqlCaps with
        {
            SupportsBackup = false,
        };

        // Cassandra / ScyllaDB: keyspace ≈ database, table, row. We provision with auth disabled
        // for v1 (cassandra's auth bootstrap is non-trivial). Full row CRUD is supported: the schema
        // service addresses rows by their full primary key (from schema metadata) and guards writes
        // with LWT IF EXISTS / IF NOT EXISTS. User mgmt + backup remain out of scope.
        private static readonly EngineCapabilitiesDto CassandraCaps = SqlCaps with
        {
            DatabaseTerm = "keyspace",
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // CouchDB: HTTP doc store. "databases" are real, but docs live straight inside them
        // (no tables). We expose a single virtual "_all_docs" table per database, similar
        // to how Redis exposes "keys".
        private static readonly EngineCapabilitiesDto CouchDbCaps = SqlCaps with
        {
            TableTerm = "collection",
            RowTerm = "document",
            SupportsListTables = true,
            SupportsDropTable = false,
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // Neo4j: graph store. "database" maps to the Neo4j database (since 4.0); "table" maps
        // to a node label; "row" maps to a node. Edges aren't browseable here; this is a
        // limited but consistent view. Edit/delete need node id + property knowledge - skip.
        private static readonly EngineCapabilitiesDto Neo4jCaps = SqlCaps with
        {
            TableTerm = "label",
            RowTerm = "node",
            SupportsCreateDatabase = false,  // Neo4j Community is single-database
            SupportsDropDatabase = false,
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // Qdrant: vector DB. Collections = tables, points = rows (id + payload). Payload edit and
        // point delete work by id; insert stays off because a new point requires a vector, which
        // the generic row form can't supply.
        private static readonly EngineCapabilitiesDto QdrantCaps = SqlCaps with
        {
            DatabaseTerm = "cluster",
            TableTerm = "collection",
            RowTerm = "point",
            SupportsCreateDatabase = false,
            SupportsDropDatabase = false,
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // SeaweedFS: blob store via its S3 gateway. Buckets = databases, one virtual
        // "_all_objects" collection per bucket, objects = rows (key/size/modified/etag).
        // Deleting a row deletes the object; upload/edit need a file UI - out of scope for v1.
        private static readonly EngineCapabilitiesDto SeaweedFsCaps = SqlCaps with
        {
            DatabaseTerm = "bucket",
            TableTerm = "collection",
            RowTerm = "object",
            SupportsDropTable = false,
            SupportsRowInsert = false,
            SupportsRowEdit = false,
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // Azurite (Azure Blob emulator): containers = databases, one virtual "_all_blobs"
        // collection per container, blobs = rows. Same shape as SeaweedFS but over the Blob API.
        private static readonly EngineCapabilitiesDto AzuriteCaps = SeaweedFsCaps with
        {
            DatabaseTerm = "container",
            RowTerm = "blob",
        };

        // Redis: 16 fixed DB numbers, no logical-DB CRUD; key-value rows; no users in our model.
        private static readonly EngineCapabilitiesDto RedisCaps = new()
        {
            DatabaseTerm = "DB number",
            TableTerm = "namespace",
            RowTerm = "key",
            SupportsListDatabases = true,
            SupportsCreateDatabase = false,
            SupportsDropDatabase = true,   // FLUSHDB
            SupportsListTables = true,     // virtual "keys" namespace per DB
            SupportsDropTable = false,
            SupportsRowRead = true,
            SupportsRowInsert = true,
            SupportsRowEdit = true,
            SupportsRowDelete = true,
            SupportsUsers = false,
            SupportsBackup = true,
        };

        // Plugin catalog. Engines reference these by key to opt in.
        private static readonly EnginePluginDto PgVector = new()
        {
            Key = "vector",
            DisplayName = "pgvector",
            Description = "Vector similarity search - pgvector extension. Image swaps to pgvector/pgvector.",
            InstallMode = PluginInstallMode.DockerImageSwap,
            Payload = "pgvector/pgvector",
        };
        private static readonly EnginePluginDto PgPostgis = new()
        {
            Key = "postgis",
            DisplayName = "PostGIS",
            Description = "Geospatial types and indexes. Image swaps to postgis/postgis.",
            InstallMode = PluginInstallMode.DockerImageSwap,
            Payload = "postgis/postgis",
        };
        private static readonly EnginePluginDto PgTrgm = new()
        {
            Key = "pg_trgm",
            DisplayName = "pg_trgm",
            Description = "Trigram-based fuzzy text search.",
            InstallMode = PluginInstallMode.PgExtension,
            Payload = "pg_trgm",
        };
        private static readonly EnginePluginDto PgUuid = new()
        {
            Key = "uuid-ossp",
            DisplayName = "uuid-ossp",
            Description = "UUID generation functions (uuid_generate_v4 etc.).",
            InstallMode = PluginInstallMode.PgExtension,
            Payload = "\"uuid-ossp\"",
        };
        private static readonly EnginePluginDto PgHstore = new()
        {
            Key = "hstore",
            DisplayName = "hstore",
            Description = "Key/value pair storage as a single column.",
            InstallMode = PluginInstallMode.PgExtension,
            Payload = "hstore",
        };

        private static readonly EnginePluginDto RedisStack = new()
        {
            Key = "redis-stack",
            DisplayName = "Redis Stack",
            Description = "Bundles RedisJSON, RediSearch, RedisTimeSeries, and RedisBloom modules.",
            InstallMode = PluginInstallMode.DockerImageSwap,
            Payload = "redis/redis-stack-server",
        };

        private static readonly EnginePluginDto Neo4jApoc = new()
        {
            Key = "apoc",
            DisplayName = "APOC",
            Description = "Utility procedures and functions (graph operations, data import, etc.).",
            InstallMode = PluginInstallMode.EnvFlag,
            Payload = "NEO4J_PLUGINS=[\"apoc\"]",
        };
        private static readonly EnginePluginDto Neo4jGds = new()
        {
            Key = "graph-data-science",
            DisplayName = "Graph Data Science",
            Description = "Graph algorithms library (PageRank, community detection, embeddings, ML).",
            InstallMode = PluginInstallMode.EnvFlag,
            Payload = "NEO4J_PLUGINS=[\"graph-data-science\"]",
        };

        // Lookup helper: every plugin we ship, indexed by key. CreateDatabaseCommand resolves
        // selections through this map.
        public static IReadOnlyDictionary<string, EnginePluginDto> AllPluginsByKey { get; } = new[]
            {
                PgVector, PgPostgis, PgTrgm, PgUuid, PgHstore,
                RedisStack,
                Neo4jApoc, Neo4jGds,
            }.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyDictionary<string, EnginePluginDto[]> PluginsByEngine = new Dictionary<string, EnginePluginDto[]>
        {
            ["postgres"] = new[] { PgVector, PgPostgis, PgTrgm, PgUuid, PgHstore },
            ["timescaledb"] = new[] { PgPostgis, PgTrgm, PgUuid, PgHstore },
            ["redis"] = new[] { RedisStack },
            ["neo4j"] = new[] { Neo4jApoc, Neo4jGds },
        };

        // Engine order drives the Create wizard tile grid. Headlining engines (PostgreSQL,
        // MariaDB, MongoDB) come first; the rest are grouped by storage shape (relational,
        // wide-column, document, graph, key-value, search, time-series, vector) without text
        // labels so the wizard renders a tidy progression by family.
        private static readonly (string Key, string DisplayName, string Image, EngineCapabilitiesDto Caps)[] Engines =
        {
            // Headliners
            ("postgres",    "PostgreSQL",  "postgres",              SqlCaps),
            ("mariadb",     "MariaDB",     "mariadb",               SqlCaps),
            ("mongo",       "MongoDB",     "mongo",                 MongoCaps),
            // Other relational SQL
            ("mysql",       "MySQL",       "mysql",                 SqlCaps),
            ("mssql",       "SQL Server",  "mcr.microsoft.com/mssql/server", SqlCaps),
            ("cockroachdb", "CockroachDB", "cockroachdb/cockroach", CockroachCaps),
            ("timescaledb", "TimescaleDB", "timescale/timescaledb", SqlCaps),
            // Analytical SQL
            ("clickhouse",  "ClickHouse",  "clickhouse/clickhouse-server", ClickhouseCaps),
            // Wide-column
            ("cassandra",   "Cassandra",   "cassandra",             CassandraCaps),
            // Document
            ("couchdb",     "CouchDB",     "couchdb",               CouchDbCaps),
            // Graph
            ("neo4j",       "Neo4j",       "neo4j",                 Neo4jCaps),
            // Key-value / cache
            ("redis",       "Redis",       "redis",                 RedisCaps),
            ("valkey",      "Valkey",      "valkey/valkey",         RedisCaps),
            // Vector
            ("qdrant",      "Qdrant",      "qdrant/qdrant",         QdrantCaps),
            // Blob / object storage
            ("seaweedfs",   "SeaweedFS",   "chrislusf/seaweedfs",   SeaweedFsCaps),
            ("azurite",     "Azure Blob (Azurite)", "mcr.microsoft.com/azure-storage/azurite", AzuriteCaps),
        };

        public async ValueTask<IReadOnlyList<SupportedDatabaseDto>> Handle(GetSupportedDatabasesQuery query, CancellationToken cancellationToken)
        {
            var result = new List<SupportedDatabaseDto>(Engines.Length);
            foreach (var (key, displayName, image, caps) in Engines)
            {
                var supportedVersions = await versions.GetSupportedVersionsAsync(key, cancellationToken);
                var plugins = PluginsByEngine.TryGetValue(key, out var p) ? p : Array.Empty<EnginePluginDto>();
                result.Add(new SupportedDatabaseDto
                {
                    Key = key,
                    DisplayName = displayName,
                    Image = image,
                    Versions = supportedVersions,
                    Capabilities = caps,
                    Plugins = plugins,
                });
            }
            return result;
        }
    }
}

using Mediator;
using KRINT.Application.Dtos;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries
{
    public record GetSupportedDatabasesQuery : IQuery<IReadOnlyList<SupportedDatabaseDto>>;

    public class GetSupportedDatabasesQueryHandler : IQueryHandler<GetSupportedDatabasesQuery, IReadOnlyList<SupportedDatabaseDto>>
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

        // Mongo: docs not rows; current implementation lacks per-document edit/insert/delete.
        private static readonly EngineCapabilitiesDto MongoCaps = SqlCaps with
        {
            TableTerm = "collection",
            RowTerm = "document",
            SupportsRowInsert = false,
            SupportsRowEdit = false,
            SupportsRowDelete = false,
        };

        // CockroachDB: Postgres-wire-compatible but no `ctid` and no pg_dump. We disable row
        // edit/delete (those use ctid to enforce exactly-one-match) and disable backup until
        // a cockroach-native dump path is added.
        private static readonly EngineCapabilitiesDto CockroachCaps = SqlCaps with
        {
            SupportsRowEdit = false,
            SupportsRowDelete = false,
            SupportsBackup = false,
        };

        // ClickHouse: SQL-shaped (databases / tables / rows / users), but row-level UPDATE/DELETE
        // is implemented as async mutations and there's no clean "edit exactly this row" path
        // without primary-key introspection. INSERT works fine. Backup needs clickhouse-backup
        // or BACKUP TO Disk - also out of scope for v1.
        private static readonly EngineCapabilitiesDto ClickhouseCaps = SqlCaps with
        {
            SupportsRowEdit = false,
            SupportsRowDelete = false,
            SupportsBackup = false,
        };

        // Cassandra / ScyllaDB: keyspace ≈ database, table, row. We provision with auth disabled
        // for v1 (cassandra's auth bootstrap is non-trivial). Row edit/delete excluded because
        // CQL UPDATE/DELETE without the full PK is unsafe; user mgmt out of scope.
        private static readonly EngineCapabilitiesDto CassandraCaps = SqlCaps with
        {
            DatabaseTerm = "keyspace",
            SupportsRowEdit = false,
            SupportsRowDelete = false,
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // Elasticsearch / OpenSearch: indices look like tables; docs look like rows but the JSON
        // shape is per-document, so we render each doc as a single JSON column (like Mongo).
        // Per-doc edit/delete needs _id lookup; skipping for v1.
        private static readonly EngineCapabilitiesDto ElasticCaps = SqlCaps with
        {
            DatabaseTerm = "cluster",
            TableTerm = "index",
            RowTerm = "document",
            SupportsCreateDatabase = false,   // there's just one "_cluster"
            SupportsDropDatabase = false,
            SupportsRowEdit = false,
            SupportsRowDelete = false,
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // ArangoDB: multi-model - databases, collections, documents over HTTP/JSON. Fits the
        // existing model cleanly except for doc edit/delete which need _key + _rev.
        private static readonly EngineCapabilitiesDto ArangoCaps = SqlCaps with
        {
            TableTerm = "collection",
            RowTerm = "document",
            SupportsRowEdit = false,
            SupportsRowDelete = false,
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
            SupportsRowEdit = false,
            SupportsRowDelete = false,
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
            SupportsRowEdit = false,
            SupportsRowDelete = false,
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // InfluxDB v2: orgs -> buckets -> points. We expose the single init-org as the database,
        // buckets as tables, and points as rows. Edit/delete are deliberate per-point ops in
        // line protocol - skip for v1. Insert via line protocol writes.
        private static readonly EngineCapabilitiesDto InfluxCaps = SqlCaps with
        {
            DatabaseTerm = "org",
            TableTerm = "bucket",
            RowTerm = "point",
            SupportsCreateDatabase = false,
            SupportsDropDatabase = false,
            SupportsRowEdit = false,
            SupportsRowDelete = false,
            SupportsRowInsert = false,
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // Solr / Meilisearch: HTTP search engines. Same shape as Elasticsearch - single
        // virtual cluster "database", cores/indexes are tables, documents are rows.
        private static readonly EngineCapabilitiesDto SolrCaps = ElasticCaps;
        private static readonly EngineCapabilitiesDto MeilisearchCaps = ElasticCaps;

        // Couchbase: SQL-shaped via N1QL but the topology is bucket → scope → collection. We
        // expose buckets as databases and collections as tables. Cluster init has to run on
        // first provision (see CreateDatabaseCommand). User mgmt skipped for v1.
        private static readonly EngineCapabilitiesDto CouchbaseCaps = SqlCaps with
        {
            DatabaseTerm = "bucket",
            TableTerm = "collection",
            RowTerm = "document",
            SupportsCreateDatabase = false,
            SupportsDropDatabase = false,
            SupportsRowEdit = false,
            SupportsRowDelete = false,
            SupportsRowInsert = false,
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // Qdrant: vector DB. We expose collections as "tables" and points as "rows" (id + payload + vector preview).
        // No multi-DB concept; insert/edit/delete are out of scope until we have a vector-aware UI.
        private static readonly EngineCapabilitiesDto QdrantCaps = SqlCaps with
        {
            DatabaseTerm = "cluster",
            TableTerm = "collection",
            RowTerm = "point",
            SupportsCreateDatabase = false,
            SupportsDropDatabase = false,
            SupportsRowEdit = false,
            SupportsRowInsert = false,
            SupportsRowDelete = false,
            SupportsUsers = false,
            SupportsBackup = false,
        };

        // etcd: distributed key-value store. Flat namespace (no DBs, no tables), keys are strings,
        // values are bytes. We model it as one virtual database ("default") with one virtual
        // table ("keys") to keep the browse contract honest.
        private static readonly EngineCapabilitiesDto EtcdCaps = new()
        {
            DatabaseTerm = "namespace",
            TableTerm = "keyspace",
            RowTerm = "key",
            SupportsListDatabases = true,
            SupportsCreateDatabase = false,
            SupportsDropDatabase = false,
            SupportsListTables = true,
            SupportsDropTable = false,
            SupportsRowRead = true,
            SupportsRowInsert = true,
            SupportsRowEdit = true,
            SupportsRowDelete = true,
            SupportsUsers = false,
            SupportsBackup = false,
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
            ("scylladb",    "ScyllaDB",    "scylladb/scylla",       CassandraCaps),
            // Document
            ("couchdb",     "CouchDB",     "couchdb",               CouchDbCaps),
            ("couchbase",   "Couchbase",   "couchbase",             CouchbaseCaps),
            // Multi-model + graph
            ("arangodb",    "ArangoDB",    "arangodb",              ArangoCaps),
            ("neo4j",       "Neo4j",       "neo4j",                 Neo4jCaps),
            // Key-value / cache
            ("redis",       "Redis",       "redis",                 RedisCaps),
            ("valkey",      "Valkey",      "valkey/valkey",         RedisCaps),
            ("etcd",        "etcd",        "quay.io/coreos/etcd",   EtcdCaps),
            // Search
            ("elasticsearch", "Elasticsearch", "elasticsearch",     ElasticCaps),
            ("opensearch",  "OpenSearch",  "opensearchproject/opensearch", ElasticCaps),
            ("solr",        "Apache Solr", "solr",                  SolrCaps),
            ("meilisearch", "Meilisearch", "getmeili/meilisearch",  MeilisearchCaps),
            // Time-series
            ("influxdb",    "InfluxDB",    "influxdb",              InfluxCaps),
            // Vector
            ("qdrant",      "Qdrant",      "qdrant/qdrant",         QdrantCaps),
        };

        private readonly IDatabaseVersionService _versions;

        public GetSupportedDatabasesQueryHandler(IDatabaseVersionService versions)
        {
            _versions = versions;
        }

        public async ValueTask<IReadOnlyList<SupportedDatabaseDto>> Handle(GetSupportedDatabasesQuery query, CancellationToken cancellationToken)
        {
            var result = new List<SupportedDatabaseDto>(Engines.Length);
            foreach (var (key, displayName, image, caps) in Engines)
            {
                var versions = await _versions.GetSupportedVersionsAsync(key, cancellationToken);
                var plugins = PluginsByEngine.TryGetValue(key, out var p) ? p : Array.Empty<EnginePluginDto>();
                result.Add(new SupportedDatabaseDto
                {
                    Key = key,
                    DisplayName = displayName,
                    Image = image,
                    Versions = versions,
                    Capabilities = caps,
                    Plugins = plugins,
                });
            }
            return result;
        }
    }
}

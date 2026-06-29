# Supported engines

KRINT supports 16 engines. **Every** engine lets you browse data and use the container console (live
logs + an interactive shell). The table below shows the capabilities that differ between engines.

| Engine | Edit rows | Multiple DBs | Users & grants | Backups | Plugins |
| --- | :---: | :---: | :---: | :---: | :---: |
| PostgreSQL | ✅ | ✅ | ✅ | ✅ | ✅ |
| MariaDB | ✅ | ✅ | ✅ | ✅ | ❌ |
| MySQL | ✅ | ✅ | ✅ | ✅ | ❌ |
| SQL Server | ✅ | ✅ | ✅ | ✅ | ❌ |
| MongoDB | ✅ | ✅ | ✅ | ✅ | ❌ |
| TimescaleDB | ✅ | ✅ | ✅ | ✅ | ✅ |
| CockroachDB | ✅ | ✅ | ✅ | ❌ | ❌ |
| ClickHouse | ✅ | ✅ | ✅ | ❌ | ❌ |
| Cassandra | ✅ | ✅ | ❌ | ❌ | ❌ |
| CouchDB | ✅ | ✅ | ❌ | ❌ | ❌ |
| Redis | ✅ | ❌ | ❌ | ✅ | ✅ |
| Valkey | ✅ | ❌ | ❌ | ✅ | ❌ |
| Qdrant | ✅ | ❌ | ❌ | ❌ | ❌ |
| Neo4j | ❌ | ❌ | ❌ | ❌ | ✅ |
| SeaweedFS (S3) | ⬆️ | ✅ | ❌ | ❌ | ❌ |
| Azurite (Azure Blob) | ⬆️ | ✅ | ❌ | ❌ | ❌ |

- **Edit rows** - in-cell editing of table rows. ⬆️ = object stores use file upload instead.
- **Multiple DBs** - create more than one database/keyspace/bucket per instance.
- **Users & grants** - manage logins and per-database access.
- **Backups** - scheduled or manual dump/restore, plus in-place version upgrade.
- **Plugins** - opt-in extensions at provision time: pgvector, PostGIS, pg_trgm and more (Postgres),
  Redis Stack (Redis), APOC and Graph Data Science (Neo4j).

::: info
Engines use their own terms in the UI - Mongo shows collections/documents, Redis shows DB
numbers/keys, Neo4j shows labels/nodes, object stores show buckets/objects - and controls that
don't apply to an engine simply don't appear.
:::

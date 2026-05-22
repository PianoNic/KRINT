<p align="center">
  <img src="assets/krint-icon.svg" width="180" alt="KRINT Logo" />
</p>
<p align="center">
  <strong>KRINT - Keyed - Replicated - Isolated - Networked - Transactional</strong><br/>
  One click. One key. Your database is ready.
</p>
<p align="center">
  <a href="https://github.com/PianoNic/KRINT"><img src="https://badgetrack.pianonic.ch/badge?tag=krint&label=visits&color=0d1117&style=flat" alt="visits" /></a>
  <a href="#-getting-started"><img src="https://img.shields.io/badge/Self--Host-Instructions-0d1117.svg" alt="Self-hosting" /></a>
  <a href="#-tech-stack"><img src="https://img.shields.io/badge/.NET-10-0d1117.svg" alt=".NET 10" /></a>
  <a href="#-tech-stack"><img src="https://img.shields.io/badge/Angular-21-0d1117.svg" alt="Angular 21" /></a>
</p>

---

> **Heads up:** KRINT is under active development. The main branch builds end-to-end and the 23 supported engines provision cleanly, but features land here before they're documented.

## What is KRINT?

KRINT is a self-hosted database-provisioning platform. Pick an engine, click Launch, and KRINT spins up a containerised instance with credentials, a host port, and a connection string already in hand. Browse rows, manage users, schedule backups, install extensions - all from the SPA.

Every instance ships with the five properties that give the project its name:

| Letter | Property      | What it means                                              |
| ------ | ------------- | ---------------------------------------------------------- |
| **K**  | Keyed         | Each instance gets a unique connection string + vaulted credentials |
| **R**  | Replicated    | Built-in backups with cron scheduling and one-click restore |
| **I**  | Isolated      | Each engine runs in its own Docker container               |
| **N**  | Networked     | Connection string ready to paste into your app             |
| **T**  | Transactional | Real databases - no toys, no mocks                         |

## Features

- **23 supported engines** out of the box - PostgreSQL, MariaDB, MongoDB, MySQL, SQL Server, CockroachDB, TimescaleDB, ClickHouse, Cassandra, ScyllaDB, CouchDB, Couchbase, ArangoDB, Neo4j, Redis, Valkey, etcd, Elasticsearch, OpenSearch, Apache Solr, Meilisearch, InfluxDB, Qdrant.
- **Plugin store** - opt-in extensions during provision: pgvector / PostGIS / pg_trgm for Postgres, Redis Stack modules, APOC / Graph Data Science for Neo4j, and more.
- **Row browser** - list, edit, insert, delete rows across SQL engines; document/keyspace browsing for Mongo, Cassandra, Redis, Couchbase, etc.
- **Backups + scheduling** - manual or cron-scheduled; download as a file or restore in place. UTC-aware cron with friendly presets.
- **User & access management** - create logins, reset passwords, grant access per logical database.
- **OIDC / Keycloak** auth out of the box (admin/admin in the bundled dev realm).
- **Capability-aware UI** - engines without a concept of "users" or "rows" simply don't show those controls.

## Screenshots

<p align="center">
  <em>Add screenshots here once you have a deploy you like.</em>
</p>

## Tech stack

- **.NET 10** ASP.NET Core API (Mediator pattern, EF Core, Clean Architecture: API / Application / Domain / Infrastructure)
- **Angular 21** + Signals + Spartan UI (helm/brain) for the SPA
- **Docker.DotNet** drives container lifecycle (create, start, exec, tar-extract for restores)
- **Keycloak** for OIDC; Keycloakify for the bundled login theme
- **TUnit** + **Microsoft.Playwright** for end-to-end tests against a live stack
- **OpenAPI** at `/openapi/v1.json`; the Angular client is regenerated via `bun run apigen`

## Project layout

```
src/
├── KRINT.API/             ASP.NET Core entry point, controllers, Dockerfile
├── KRINT.Application/     Commands, queries, DTOs (Mediator handlers)
├── KRINT.Domain/          Entities, value objects, domain rules
├── KRINT.Infrastructure/  Docker, per-engine inner services, persistence, secrets vault
├── KRINT.Frontend/        Angular 21 SPA
└── KRINT.Tests/           TUnit + Playwright E2E
keycloak/                  Custom theme (Keycloakify) + realm config
```

Dependencies flow inward: `API -> Application -> Domain`. `Infrastructure` implements interfaces declared in `Application` / `Domain`.

## Getting started

### Prerequisites

- Docker Desktop (with Docker daemon reachable on the default socket)
- .NET 10 SDK
- Bun (or Node) for the frontend
- Optional: Keycloak running on `http://localhost:8080` with the bundled `krint` realm

### Run the API

```powershell
dotnet run --project src/KRINT.API
```

Default URLs (see `src/KRINT.API/Properties/launchSettings.json`):

- HTTP: http://localhost:5165
- HTTPS: https://localhost:7064
- OpenAPI doc: `/openapi/v1.json`

### Run the frontend

```powershell
cd src/KRINT.Frontend
bun install
bun start          # ng serve on http://localhost:4200
bun run apigen     # regenerate the API client after backend changes
```

### Run in Docker

```powershell
docker build -t krint-api -f src/KRINT.API/Dockerfile src/KRINT.API
docker run --rm -p 8080:8080 -p 8081:8081 krint-api
```

The full stack (Keycloak + API + frontend) lives in [`docs/dev-setup.md`](docs/dev-setup.md).

## Testing

```powershell
dotnet run --project src/KRINT.Tests
```

`KRINT.Tests` uses [TUnit](https://github.com/thomhurst/TUnit). The suite includes both unit tests and a Playwright-driven browser E2E (`E2E/KrintEndToEndTests.cs`) that exercises the wizard, instance dialogs, backups, and activity log against a live Keycloak + API + frontend.

## License

TBD.

---

<p align="center">Made with care by <a href="https://github.com/PianoNic">PianoNic</a></p>

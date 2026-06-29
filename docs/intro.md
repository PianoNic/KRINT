# What is KRINT?

KRINT is a self-hosted platform for provisioning and operating databases. Pick an engine, click Launch, and you get a containerised instance with credentials, a host port, and a connection string already in hand.

- **Provision in one click**. 16 engines (Postgres, MySQL, Mongo, Redis, and more), each with opt-in plugins like pgvector and PostGIS.
- **Browse and query**. Spreadsheet-style in-cell row editing and an ad-hoc SQL console, in the browser.
- **Container console**. Live log tailing and an interactive shell into any provisioned instance.
- **Back up and upgrade**. Manual or cron-scheduled dumps; restore or upgrade the engine version in place.
- **Manage users and access**. Create logins, reset passwords, grant per-database access.
- **Distribute across nodes**. Provision databases onto remote Docker hosts over a single connection.
- **Bring your own auth**. OIDC with any provider (Pocket ID, Authentik, Auth0…), or the bundled Keycloak.

There is no limit to the number of databases or nodes you can run.

## Architecture

KRINT runs from one image in one of two roles.

| Role | What it does |
| --- | --- |
| **Control plane** (default) | The full app: UI, API, metadata database, and the secrets vault. All user interaction flows through it. |
| **Node** (`Krint__Role=node`) | A stripped, stateless worker that runs database containers on its own host and dials **out** to the control plane over one SignalR connection. Owns no state. |

Each database is provisioned as an isolated **sibling container** on the Docker host - not a child of KRINT - which is why KRINT mounts the Docker socket.

## Distributions

KRINT ships two ways from the same codebase.

| Distribution | Metadata DB | Auth | Use case |
| --- | --- | --- | --- |
| **Docker image** | Postgres or SQLite | OIDC (bring-your-own or bundled Keycloak) | Servers, multi-user, always-on |
| **Desktop app** | SQLite | Built-in, zero-config | A single user on their own machine |

## Get started

- **[Self-hosting](./self-host)** - run the image with `docker compose`.
- **[Desktop app](./desktop)** - the single-user build.
- **[Developer setup](./dev-setup)** - local dev with `dotnet run` + Bun.

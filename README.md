<p align="center">
  <img src="assets/krint-icon.svg" width="180" alt="KRINT Logo" />
</p>
<p align="center">
  <strong>KRINT</strong><br/>
  One click. One key. Your database is ready.
</p>
<p align="center">
  <a href="https://github.com/PianoNic/KRINT"><img src="https://badgetrack.pianonic.ch/badge?tag=krint&label=visits&color=0d1117&style=flat" alt="visits" /></a>
  <a href="https://docs.krint.pianonic.ch/self-host"><img src="https://img.shields.io/badge/Self--Host-Instructions-0d1117.svg" alt="Self-hosting" /></a>
  <img src="https://img.shields.io/badge/.NET-10-0d1117.svg" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Angular-21-0d1117.svg" alt="Angular 21" />
</p>

---

> **Heads up:** KRINT is in early development. Expect rough edges and breaking changes between versions.

## What is KRINT?

KRINT is a self-hosted database-provisioning platform. Pick an engine, click Launch, and you get a containerised instance with credentials, a host port, and a connection string in hand. Then browse rows, run queries, manage users, and schedule backups - all from one UI.

## Screenshots

<p align="center">
  <img src="assets/screenshots/home.png" width="49%" alt="Live dashboard" />
  <img src="assets/screenshots/instances.png" width="49%" alt="Instances list" />
</p>
<p align="center">
  <img src="assets/screenshots/create-engine.png" width="49%" alt="Create wizard" />
  <img src="assets/screenshots/browser.png" width="49%" alt="Database browser" />
</p>

<details>
<summary><strong>Show more screenshots</strong></summary>

<p align="center">
  <img src="assets/screenshots/query.png" width="49%" alt="Query console" />
  <img src="assets/screenshots/create-plugins.png" width="49%" alt="Create wizard - plugins step" />
</p>
<p align="center">
  <img src="assets/screenshots/console-logs.png" width="49%" alt="Container logs streaming" />
  <img src="assets/screenshots/console-exec.png" width="49%" alt="Interactive container shell" />
</p>
<p align="center">
  <img src="assets/screenshots/backups.png" width="49%" alt="Backups and schedules" />
  <img src="assets/screenshots/backup-schedule-dialog.png" width="49%" alt="Schedule a backup" />
</p>
<p align="center">
  <img src="assets/screenshots/instance-details.png" width="49%" alt="Instance details" />
  <img src="assets/screenshots/instance-edit.png" width="49%" alt="Edit instance: databases + users" />
</p>
<p align="center">
  <img src="assets/screenshots/instance-upgrade.png" width="49%" alt="Engine version upgrade" />
  <img src="assets/screenshots/activity.png" width="49%" alt="Activity log" />
</p>
<p align="center">
  <img src="assets/screenshots/nodes.png" width="49%" alt="Nodes: remote Docker workers" />
  <img src="assets/screenshots/settings.png" width="49%" alt="Settings" />
</p>

</details>

## Features

- **16 engines** + opt-in plugins (pgvector, PostGIS, Redis Stack, APOC, and more).
- **Browse & query**: in-cell row editing and an ad-hoc SQL console.
- **Container console**: live log tailing and an interactive shell, in the browser.
- **Backups**: manual, scheduled, or upload your own; restore or upgrade in place.
- **Users & access**: logins, password resets, per-database grants.
- **OIDC auth**: bring your own provider or use the bundled Keycloak.
- **Nodes** (experimental): provision onto remote Docker hosts over one connection. See [docs/nodes.md](docs/nodes.md).

## Get started

- 📦 **[Self-hosting guide](https://docs.krint.pianonic.ch/self-host)** - run the image with `docker compose`.
- 🛠️ **[Developer setup](https://docs.krint.pianonic.ch/dev-setup)** - local dev with `dotnet run` + Bun, migrations, tests.

Full documentation: **[docs.krint.pianonic.ch](https://docs.krint.pianonic.ch)**

<details>
<summary><strong>Tech stack</strong></summary>

- **.NET 10** ASP.NET Core API (Mediator, EF Core, Clean Architecture).
- **Angular 21** + Signals + Spartan UI.
- **SignalR** for the live dashboard, log tailing, and interactive shell; **xterm.js** for the console.
- **Docker.DotNet** for container lifecycle.
- **Keycloak** for OIDC.
- **TUnit** + **Microsoft.Playwright** for tests; **OpenAPI** client via `bun run apigen`.

</details>

## License

TBD.

---

<p align="center">Made with care by <a href="https://github.com/PianoNic">PianoNic</a></p>

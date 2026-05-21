# K.R.I.N.T.

**Keyed · Replicated · Isolated · Networked · Transactional**

Krint. One click. One key. Your database is ready.

---

## What is KRINT?

KRINT is a database-provisioning platform. Every instance it creates ships
with the five properties that give the project its name — no extra
configuration, no afterthoughts.

| Letter | Word           | What it means for the user                                       |
| ------ | -------------- | ---------------------------------------------------------------- |
| **K**  | Keyed          | Each instance gets a unique connection string + credentials      |
| **R**  | Replicated     | Built-in backups & point-in-time recovery                        |
| **I**  | Isolated       | Each DB runs in its own container, no cross-contamination        |
| **N**  | Networked      | Instantly reachable — connection string ready to use             |
| **T**  | Transactional  | Full ACID guarantees — it's a real database, not a toy           |

## Tech stack

- **.NET 10** ASP.NET Core Web API
- **Clean Architecture** — API / Application / Domain / Infrastructure split
- **Docker** — Linux container, exposes 8080 (HTTP) / 8081 (HTTPS)
- **OpenAPI** — enabled in Development via `Microsoft.AspNetCore.OpenApi`
- **TUnit** — test runner (`KRINT.Tests`, .NET 8)

## Project layout

```
src/
├── KRINT.API/             ASP.NET Core entry point, controllers, Dockerfile
├── KRINT.Application/     Use cases / application services
├── KRINT.Domain/          Entities, value objects, domain rules
├── KRINT.Infrastructure/  Persistence, container orchestration, external integrations
└── KRINT.Tests/           TUnit test suite
```

Dependencies flow inward: `API → Application → Domain`, with `Infrastructure`
implementing interfaces defined in `Application`/`Domain`.

## Running locally

```powershell
# from the repo root
dotnet run --project src/KRINT.API
```

Default URLs (see `src/KRINT.API/Properties/launchSettings.json`):

- HTTP:  http://localhost:5165
- HTTPS: https://localhost:7064
- OpenAPI doc: `/openapi/v1.json` (Development only)

## Running in Docker

```powershell
docker build -t krint-api -f src/KRINT.API/Dockerfile src/KRINT.API
docker run --rm -p 8080:8080 -p 8081:8081 krint-api
```

## Tests

```powershell
dotnet test src/KRINT.Tests
```

`KRINT.Tests` uses [TUnit](https://github.com/thomhurst/TUnit). Assembly-level
`[Retry(3)]` is configured in `GlobalSetup.cs`.

## Status

Early scaffold. The four layers exist but only contain placeholder types
(`Class1`) plus the default Weather Forecast controller. Real provisioning
logic — container orchestration, credential issuance, backup wiring — is the
next slab of work.

## License

TBD.

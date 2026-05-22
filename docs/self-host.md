# KRINT — Self-Host

Run KRINT against the pre-built image. This guide assumes:

- A Linux or Windows host with **Docker + Compose v2**
- A directory you can leave running (the data lives in named volumes)
- Outbound network access to **ghcr.io** (or Docker Hub) for the image pull, and to your engines' Docker images (`postgres`, `mysql`, etc.) when users provision instances

The image: `ghcr.io/pianonic/krint:latest` (also `pianonic/krint:latest` on Docker Hub).

---

## TL;DR

```bash
git clone https://github.com/PianoNic/KRINT.git
cd KRINT

cp .env.example .env            # then edit it - see "Environment" below
docker compose up -d            # pulls krint + postgres + keycloak, starts everything

# Wait ~30s for Keycloak to import the realm on first boot, then:
open http://localhost:5000      # KRINT UI
```

---

## What you get

Three containers managed by `compose.yml`:

| Service    | Image                                     | Purpose                                 | Default port |
| ---------- | ----------------------------------------- | --------------------------------------- | ------------ |
| `krint`    | `ghcr.io/pianonic/krint:latest`           | API + SPA in one image                  | `5000`       |
| `db`       | `postgres:18.3`                           | KRINT's app DB + Keycloak's DB          | `5432`       |
| `keycloak` | `quay.io/keycloak/keycloak:26.6`          | OIDC provider (auth)                    | `8080`       |

KRINT itself spins up **separate, isolated containers** for every database instance you provision — those are sibling containers on the Docker host, not children of `krint`. That's why the `krint` service mounts `/var/run/docker.sock`.

---

## Environment

Copy `.env.example` to `.env` and fill the values. Required:

| Variable                       | What it is                                                                                                                     |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------ |
| `POSTGRES_PASSWORD`            | Password for the shared Postgres super-user. Used by both KRINT and Keycloak.                                                  |
| `KC_BOOTSTRAP_ADMIN_USERNAME`  | Initial Keycloak admin (default `admin`).                                                                                      |
| `KC_BOOTSTRAP_ADMIN_PASSWORD`  | Initial Keycloak admin password. Change after first login.                                                                     |
| `KC_HOSTNAME`                  | Public URL Keycloak should advertise (e.g. `http://localhost:8080`, or your reverse-proxied domain).                           |
| `KC_DB_USERNAME` / `KC_DB_PASSWORD` | Keycloak's DB credentials. Easiest: reuse `postgres` / `${POSTGRES_PASSWORD}`.                                            |
| `KRINT_VAULT_KEY`              | **32 random bytes, base64-encoded.** AES-256 key for the secrets vault. Generate with `openssl rand -base64 32`.                |
| `KRINT_OIDC_AUTHORITY`         | Keycloak realm URL, e.g. `http://localhost:8080/realms/krint`.                                                                 |
| `KRINT_OIDC_REDIRECT_URI`      | The KRINT URL users return to after login, e.g. `http://localhost:5000/`.                                                      |
| `KRINT_CORS_ORIGIN`            | Browser origin allowed to call the API. Match `KRINT_OIDC_REDIRECT_URI` without the trailing slash.                            |

> **Don't lose `KRINT_VAULT_KEY`.** All provisioned-instance passwords are encrypted under this key. Lose it and you can't decrypt the vault — there is no recovery flow and **no key-rotation flow yet**, so don't rotate it once you have data.

### Generating the vault key

```bash
openssl rand -base64 32
# example output: hXp+J3kQz9N2Y... (paste into KRINT_VAULT_KEY)
```

PowerShell equivalent:

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```

---

## `.env.example`

```env
# Postgres (shared by KRINT app DB and Keycloak DB)
POSTGRES_PASSWORD=change-me

# Keycloak admin
KC_BOOTSTRAP_ADMIN_USERNAME=admin
KC_BOOTSTRAP_ADMIN_PASSWORD=change-me
KC_HOSTNAME=http://localhost:8080
KC_DB_USERNAME=postgres
KC_DB_PASSWORD=change-me

# KRINT
KRINT_VAULT_KEY=GENERATE_WITH_openssl_rand_base64_32
KRINT_OIDC_AUTHORITY=http://localhost:8080/realms/krint
KRINT_OIDC_REDIRECT_URI=http://localhost:5000/
KRINT_CORS_ORIGIN=http://localhost:5000
```

---

## `compose.yml` (self-host shape)

The `compose.yml` shipped with the repo today only includes `db` + `keycloak` — you'll need to add the `krint` service. Drop this in:

```yaml
services:
  krint:
    image: ghcr.io/pianonic/krint:latest
    container_name: krint
    restart: unless-stopped
    depends_on:
      - db
      - keycloak
    ports:
      - "5000:8080"
    environment:
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__KrintDatabase: "Host=db;Port=5432;Database=krint;Username=postgres;Password=${POSTGRES_PASSWORD}"
      Vault__MasterKey: ${KRINT_VAULT_KEY}
      Oidc__Authority: ${KRINT_OIDC_AUTHORITY}
      Oidc__ClientId: krint
      Oidc__RedirectUri: ${KRINT_OIDC_REDIRECT_URI}
      Oidc__PostLogoutRedirectUri: ${KRINT_OIDC_REDIRECT_URI}
      Oidc__Scope: "openid profile email roles"
      Oidc__RequireHttpsMetadata: "false"
      Cors__AllowedOrigins__0: ${KRINT_CORS_ORIGIN}
    volumes:
      # Docker socket - KRINT provisions DB containers as siblings on this host
      - /var/run/docker.sock:/var/run/docker.sock
      # Persistent backup directory (matches the Backup:Directory default)
      - krint-backups:/app/backups

volumes:
  krint-backups:
```

> Windows hosts: replace the socket mount with `//var/run/docker.sock:/var/run/docker.sock` (the leading double-slash is what Docker Desktop's Linux engine needs).

---

## First-run checklist

1. **Pull + start:** `docker compose up -d`
2. **Wait for Keycloak.** First boot imports the `krint` realm and seeds the OIDC client — give it 30–60s. Tail with `docker compose logs -f keycloak` and wait for `Listening on: http://0.0.0.0:8080`.
3. **Create your first user.** Open `http://localhost:8080`, log in as the admin from `.env`, switch to the **krint** realm, **Users → Add user**, set a password under **Credentials**.
4. **Log in to KRINT.** Open `http://localhost:5000`. You'll be redirected to Keycloak, authenticate, and land back on the dashboard.
5. **Provision an instance.** Click **Create**, pick an engine, click Launch. KRINT pulls the engine image, starts a sibling container, generates credentials, and hands you a connection string.

---

## Upgrading

```bash
docker compose pull krint
docker compose up -d krint
```

KRINT runs EF migrations on startup, so schema changes apply automatically. The vault, secrets, and instance metadata in the app DB are preserved across upgrades; provisioned-instance containers and their volumes are untouched (they're independent of the `krint` container's lifecycle).

To pin a version, replace `:latest` with a tag — see https://github.com/PianoNic/KRINT/pkgs/container/krint for the published tags (`0.1.0`, `0.1`, `0`, `latest`).

---

## Data persistence

| Volume                | What it holds                                                                                                   |
| --------------------- | --------------------------------------------------------------------------------------------------------------- |
| `postgres-data`       | KRINT's metadata DB + Keycloak's DB (users, sessions, realm config).                                            |
| `keycloak-data`       | Keycloak's writeable state (kept separately from the DB).                                                       |
| `krint-backups`       | Dumps written by **Backups** for instances that support `pg_dump` / `mysqldump` / `mongodump` / Redis snapshot. |
| *(per-instance)*      | Each provisioned engine gets its own auto-named volume (`krint-<engine>-<id>-data`).                            |

Back up the first three before any major upgrade. Provisioned-instance volumes are independent and survive `docker compose down` of the KRINT stack.

---

## Reverse proxy (optional)

If you're putting KRINT behind a reverse proxy (Caddy, Traefik, nginx), you need:

1. **Public origin** matches `KRINT_OIDC_REDIRECT_URI` and `KRINT_CORS_ORIGIN`. Update both if your domain changes.
2. **Keycloak's `KC_HOSTNAME`** must be the public Keycloak URL — the OIDC tokens carry the `iss` claim from this value, and KRINT validates it.
3. **Trust `X-Forwarded-*`** headers in the proxy. The compose already sets `KC_PROXY_HEADERS=xforwarded` on Keycloak; KRINT's API trusts forwarded headers via ASP.NET defaults.
4. **WebSockets** aren't used today, so no special websocket config is needed.

Example Caddyfile snippet:

```caddy
krint.example.com {
  reverse_proxy krint:8080
}
sso.example.com {
  reverse_proxy keycloak:8080
}
```

Then in `.env`:

```env
KC_HOSTNAME=https://sso.example.com
KRINT_OIDC_AUTHORITY=https://sso.example.com/realms/krint
KRINT_OIDC_REDIRECT_URI=https://krint.example.com/
KRINT_CORS_ORIGIN=https://krint.example.com
```

---

## Troubleshooting

| Symptom                                                                          | Likely cause / fix                                                                                                           |
| -------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `krint` exits with `Vault:MasterKey must decode to 32 bytes`                     | `KRINT_VAULT_KEY` is empty or not a base64-encoded 32-byte value. Regenerate with `openssl rand -base64 32`.                 |
| Login loop / `iss` mismatch                                                      | `KC_HOSTNAME` and `KRINT_OIDC_AUTHORITY` don't agree on the same public Keycloak URL.                                        |
| CORS error in the browser console                                                | `KRINT_CORS_ORIGIN` doesn't match the origin the browser is loading the SPA from. Trailing slashes must be excluded.         |
| Create fails with `Cannot connect to the Docker daemon`                          | The Docker socket isn't mounted into `krint`. Verify the `/var/run/docker.sock` bind exists in the running container.        |
| `No free host port in range` when provisioning                                   | `krint.yaml`'s `port_ranges` exhausted. Either delete a previous instance or expand the range and `docker compose restart krint`. |
| Keycloak admin works but the krint realm 404s                                    | Realm import didn't run — `keycloak/krint-realm.json` is missing from the build context. Pull fresh and rebuild.             |

---

## Going further

- **Backups** — schedule cron-based dumps from the Backups page (uses the `krint-backups` volume).
- **Engines and plugins** — see the engine matrix in the SPA's Create wizard or the README's Supported Engines table.
- **Hacking on it** — see [`docs/dev-setup.md`](./dev-setup.md) for the local-development setup with bun + `dotnet run`.

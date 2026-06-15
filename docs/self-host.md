# KRINT Self-Host

Run KRINT against the pre-built image. This guide assumes:

- A Linux or Windows host with **Docker + Compose v2**
- A directory you can leave running (state lives in named Docker volumes and a `./backups` bind mount)
- Outbound network access to **ghcr.io** (or Docker Hub) for the image pull, and to your engines' Docker images (`postgres`, `mysql`, etc.) when users provision instances

The image: `ghcr.io/pianonic/krint:latest` (also `pianonic/krint:latest` on Docker Hub).

---

## TL;DR

There are two flavours of self-host:

1. **Clone the repo** if you want the bundled Keycloak (zero-config auth). The repo's `compose.yml` includes Keycloak, the realm import, and the dev-mode `build:` block:
   ```bash
   git clone https://github.com/PianoNic/KRINT.git
   cd KRINT
   cp .env.example .env            # edit before first start, see "Environment"
   docker compose up -d            # postgres + keycloak + krint
   open http://localhost:5000
   ```
2. **Image-only** if you already run an OIDC provider (Pocket ID, Authentik, Auth0, ...) and just want KRINT + its app DB. Drop a single `compose.yml` (snippet below) and `.env` into any directory, no clone needed. See "Using your own OIDC provider" further down.

---

## What you get

Three containers managed by `compose.yml`:

| Service    | Image                                     | Purpose                                 | Default port |
| ---------- | ----------------------------------------- | --------------------------------------- | ------------ |
| `krint`    | `ghcr.io/pianonic/krint:latest`           | API + SPA in one image                  | `5000`       |
| `db`       | `postgres:18.3`                           | KRINT's app DB + Keycloak's DB          | `5432`       |
| `keycloak` | `quay.io/keycloak/keycloak:26.6`          | OIDC provider (auth)                    | `8080`       |

KRINT itself spins up **separate, isolated containers** for every database instance you provision. Those are sibling containers on the Docker host, not children of `krint`, which is why the `krint` service mounts `/var/run/docker.sock`.

---

## Environment

Copy `.env.example` to `.env` and fill the values. Variables use the same names the containers actually read (no compose-level rewriting), so anything you set here lands directly in the relevant container's environment.

### Keycloak

| Variable                       | What it is                                                                                                |
| ------------------------------ | --------------------------------------------------------------------------------------------------------- |
| `KC_BOOTSTRAP_ADMIN_USERNAME`  | Initial Keycloak admin (master realm). Defaults to `admin`.                                               |
| `KC_BOOTSTRAP_ADMIN_PASSWORD`  | Initial Keycloak admin password. Change after first login.                                                |
| `KC_HOSTNAME`                  | **Full** public URL Keycloak should advertise (e.g. `http://localhost:8080`, or `https://sso.example.com`). Tokens carry this in `iss`. |
| `KC_HTTP_ENABLED`              | `true` for local HTTP. Keep `true` even behind a TLS-terminating proxy.                                   |
| `KC_PROXY_HEADERS`             | `xforwarded`. Trusts `X-Forwarded-*` from a reverse proxy.                                                |
| `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` | `true`. Lets the API container fetch Keycloak metadata via the compose-network URL while browsers still see the public URL. Required for OIDC to work end-to-end. |

> DB credentials (`POSTGRES_PASSWORD`, `KC_DB_USERNAME`, `KC_DB_PASSWORD`) are **not** in `.env`. They're hardcoded in `compose.yml` because they only matter inside the compose network. Edit `compose.yml` directly if you want to change them.

### KRINT API

| Variable                       | What it is                                                                                                |
| ------------------------------ | --------------------------------------------------------------------------------------------------------- |
| `Vault__MasterKey`             | **32 random bytes, base64-encoded.** AES-256 key for the secrets vault. Generate with `openssl rand -base64 32`. |
| `Oidc__Authority`              | **Public** Keycloak realm URL, e.g. `http://localhost:8080/realms/krint`. This is what tokens carry in `iss`. |
| `Oidc__ClientId`               | `krint` (matches the realm import).                                                                       |
| `Oidc__RedirectUri`            | KRINT URL users return to after login, e.g. `http://localhost:5000/`.                                     |
| `Oidc__PostLogoutRedirectUri`  | Same value, usually.                                                                                      |
| `Oidc__Scope`                  | `openid profile email roles`.                                                                             |
| `Oidc__RequireHttpsMetadata`   | `false` for plain-HTTP dev. Set to `true` (or remove) once Keycloak is behind HTTPS.                      |
| `Cors__AllowedOrigins__0`      | Browser origin allowed to call the API. Match the SPA origin (no trailing slash). Add more as `__1`, `__2`. |

> **Don't lose `Vault__MasterKey`.** All provisioned-instance passwords are encrypted under this key. Lose it and you can't decrypt the vault. There is no recovery flow and **no key-rotation flow yet**, so don't rotate it once you have data.

### Set inside `compose.yml`, not `.env`

These live in the `krint` service's `environment:` block because they reference the compose network or include credentials that mirror `compose.yml`:

| Variable                            | Value                                                                                    |
| ----------------------------------- | ---------------------------------------------------------------------------------------- |
| `Database__Provider`                | `Postgres` for the bundled stack (set in the shipped `compose.yml`). Omit it or set `Sqlite` to use the file-based default. See "Choosing the app database" below. |
| `ConnectionStrings__KrintDatabase`  | Postgres: `Host=db;Port=5432;Database=krint;Username=postgres;Password=<the-shared-pw>`. SQLite: `Data Source=/data/krint.db` (defaults to `krint.db` if unset). |
| `Oidc__InternalAuthority`           | `http://keycloak:8080/realms/krint`. The in-cluster Keycloak URL the API fetches discovery + JWKS from. Don't change unless you rename the keycloak service. |

### Choosing the app database

KRINT stores its own metadata (provisioned-instance records, encrypted secrets, activity log,
backup schedules) in either **SQLite** or **PostgreSQL**, picked with `Database__Provider`:

| Provider | When to use it | Config |
| -------- | -------------- | ------ |
| `Sqlite` (default) | Single-node self-host, simplest setup. No extra container - data lives in one file. | `Database__Provider=Sqlite` and a writable path, e.g. `ConnectionStrings__KrintDatabase=Data Source=/data/krint.db`. |
| `Postgres` | Shared with the bundled Keycloak DB, or if you already run Postgres. Used by the shipped `compose.yml`. | `Database__Provider=Postgres` and a Npgsql `ConnectionStrings__KrintDatabase`. |

The schema is created and migrated automatically on startup for whichever provider is active, so
there's nothing to run by hand.

**Running on SQLite** (drops the `db` service entirely). In the `krint` service of your
`compose.yml`, set:

```yaml
    environment:
      Database__Provider: "Sqlite"
      ConnectionStrings__KrintDatabase: "Data Source=/data/krint.db"
    volumes:
      - ./db:/data        # persist the SQLite file across restarts
```

Then remove the `db:` service and the `depends_on: db` from the `krint` service. Keep the Keycloak
DB separate (Keycloak still needs its own storage); the bundled compose co-locates both in one
Postgres, so dropping `db` only makes sense with an external/own OIDC provider.

> The SQLite file must live on a mounted volume (`/data` above). Without it the database is wiped
> every time the container is recreated.

### Generating the vault key

```bash
openssl rand -base64 32
# example output: hXp+J3kQz9N2Y... (paste into Vault__MasterKey)
```

PowerShell equivalent:

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```

---

## `.env.example`

```env
# ---------- Keycloak ----------
KC_BOOTSTRAP_ADMIN_USERNAME=admin
KC_BOOTSTRAP_ADMIN_PASSWORD=change-me

KC_HOSTNAME=http://localhost:8080
KC_HTTP_ENABLED=true
KC_PROXY_HEADERS=xforwarded
KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true

# ---------- KRINT API ----------
Vault__MasterKey=GENERATE_WITH_openssl_rand_base64_32

Oidc__Authority=http://localhost:8080/realms/krint
Oidc__ClientId=krint
Oidc__RedirectUri=http://localhost:5000/
Oidc__PostLogoutRedirectUri=http://localhost:5000/
Oidc__Scope=openid profile email roles
Oidc__RequireHttpsMetadata=false

Cors__AllowedOrigins__0=http://localhost:5000
```

---

## First-run checklist

1. **Pull + start:** `docker compose up -d`. The shipped compose waits for Postgres `pg_isready` before starting `krint`, so first boot is race-free.
2. **Wait for Keycloak.** First boot imports the `krint` realm and seeds the OIDC client, so give it 30 to 60 seconds. Tail with `docker compose logs -f keycloak` and wait for `Listening on: http://0.0.0.0:8080`.
3. **Create your first user.** Open `http://localhost:8080`, log in as the bootstrap admin from `.env`, switch to the **krint** realm, **Users → Add user**, set a password under **Credentials**, then complete the profile (first/last name) on first login.
4. **Log in to KRINT.** Open `http://localhost:5000`. You'll be redirected to Keycloak, authenticate, and land back on the dashboard.
5. **Provision an instance.** Click **Create**, pick an engine, click Launch. KRINT pulls the engine image, starts a sibling container, generates credentials, and hands you a connection string.

---

## Upgrading

```bash
docker compose pull krint
docker compose up -d krint
```

KRINT runs EF migrations on startup, so schema changes apply automatically. The vault, secrets, and instance metadata in the app DB are preserved across upgrades; provisioned-instance containers and their volumes are untouched (they're independent of the `krint` container's lifecycle).

To pin a version, replace `:latest` with a tag. See https://github.com/PianoNic/KRINT/pkgs/container/krint for the published tags (`0.1.0`, `0.1`, `0`, `latest`).

---

## Data persistence

| Location                | What it holds                                                                                                   |
| ----------------------- | --------------------------------------------------------------------------------------------------------------- |
| `postgres-data` volume  | KRINT's metadata DB + Keycloak's DB (users, sessions, realm config).                                            |
| `keycloak-data` volume  | Keycloak's writeable state (kept separately from the DB).                                                       |
| `./backups/` (bind)     | Dumps written by **Backups** for instances that support `pg_dump` / `mysqldump` / `mongodump` / Redis snapshot. Visible directly in your repo folder, gitignored. |
| *(per-instance volumes)*| Each provisioned engine gets its own auto-named volume (`krint-<engine>-<id>-data`) when `storage.mode: Volume` (default).                            |
| *(per-instance host folders)* | When `storage.mode: HostFolder`, each instance bind-mounts a subdirectory of `storage.host_path` instead — see [Storing instance data on a host folder](#storing-instance-data-on-a-host-folder). |

Back up `postgres-data`, `keycloak-data`, and `./backups/` before any major upgrade. Provisioned-instance volumes are independent and survive `docker compose down` of the KRINT stack.

### Storing instance data on a host folder

By default each provisioned instance lives in a named Docker volume that's invisible to the host filesystem. If you'd rather see and back up the raw data files yourself, switch `krint.yaml`:

```yaml
krint:
  storage:
    mode: HostFolder
    host_path: /data/krint     # absolute path on the Docker HOST (not in the KRINT container)
```

After restart, every **new** provision bind-mounts `${host_path}/${containerName}` to the engine's data directory. Existing instances keep whatever they were provisioned with.

Two gotchas:

- **Permissions**: engines like Postgres run as a specific UID inside the container and refuse to start if they can't write to the data directory. Docker handles this automatically for named volumes; for bind mounts it's on you (`chown 999:999 /data/krint`, etc.) the first time you point KRINT at a fresh folder. If a provision fails right after the container starts, check `docker logs`.
- **Delete cleanup**: KRINT calls `Directory.Delete(...)` on the subfolder when you delete an instance. If KRINT runs inside a container, it can only do that if `${host_path}` is mounted into the KRINT container at the same path — otherwise the subfolder stays behind and you clean it up by hand.

---

## Using your own OIDC provider (skip the bundled Keycloak)

The bundled Keycloak is convenient for a zero-config first run, but if you already operate an IdP (Pocket ID, Authentik, Auth0, Zitadel, Dex, plain Keycloak elsewhere, etc.) you can point KRINT at it and drop the `keycloak` service entirely.

### Requirements for the provider

KRINT's SPA uses Authorization Code Flow with **PKCE**: so register KRINT as a **public client** (no client secret). The IdP must support:

- OIDC discovery at `<authority>/.well-known/openid-configuration`
- `S256` PKCE (any modern IdP does)
- Standard `openid`, `profile`, `email` scopes
- Wildcard or exact-match redirect URIs

On the IdP side, configure the client with:

| Setting               | Value                                                                       |
| --------------------- | --------------------------------------------------------------------------- |
| Client type           | **Public** (PKCE, no secret)                                                |
| Redirect URI(s)       | `http://localhost:5000/*` (or your public KRINT URL, e.g. `https://krint.example.com/*`) |
| Post-logout URI       | Same                                                                        |
| Web origins / CORS    | The KRINT origin without the trailing slash                                 |
| Scopes                | `openid profile email`. `roles` is optional, KRINT tolerates its absence    |
| Group/user restriction| Make sure the user logging in is allowed to access the client               |

### Compose for image users (no clone, no bundled Keycloak)

Drop this `compose.yml` next to a matching `.env` (see below) and run `docker compose up -d`. Everything secret is pulled from `.env` via `${VAR}` substitution; everything stateful lives in `./db/` and `./backups/` so it's right there on the host.

```yaml
services:
  krint:
    image: ghcr.io/pianonic/krint:latest
    container_name: krint
    restart: unless-stopped
    # Required: krint runs in its own container, so localhost there is its loopback, not the
    # Docker host. The readiness probe for provisioned DB containers reaches them via
    # host.docker.internal, which host-gateway aliases to the host on Docker Engine 20.10+.
    extra_hosts:
      - "host.docker.internal:host-gateway"
    depends_on:
      db:
        condition: service_healthy
    ports:
      - "5000:8080"
    environment:
      ConnectionStrings__KrintDatabase: "Host=db;Port=5432;Database=krint;Username=postgres;Password=${POSTGRES_PASSWORD}"
      Vault__MasterKey: ${KRINT_VAULT_KEY}
      Oidc__Authority: ${KRINT_OIDC_AUTHORITY}
      Oidc__ClientId: ${KRINT_OIDC_CLIENT_ID}
      Oidc__RedirectUri: ${KRINT_OIDC_REDIRECT_URI}
      Oidc__PostLogoutRedirectUri: ${KRINT_OIDC_REDIRECT_URI}
      Oidc__Scope: "openid profile email roles"
      Oidc__RequireHttpsMetadata: "true"
      Cors__AllowedOrigins__0: ${KRINT_CORS_ORIGIN}
    volumes:
      # Docker socket - KRINT provisions DB containers as siblings on this host.
      # On Windows / Docker Desktop prefix the host side with a slash: //var/run/docker.sock
      - /var/run/docker.sock:/var/run/docker.sock
      - ./backups:/app/backups
      # Optional: only mount your own krint.yaml if you want to override port ranges etc.
      # The image ships a sensible default.
      # - ./krint.yaml:/app/krint.yaml:ro

  db:
    image: postgres:18.4
    container_name: krint-db
    restart: unless-stopped
    environment:
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: krint
    volumes:
      - ./db:/var/lib/postgresql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d krint"]
      interval: 2s
      timeout: 3s
      retries: 30
```

Matching `.env` (replace the placeholders, keep the keys):

```env
POSTGRES_PASSWORD=change-me

KRINT_VAULT_KEY=<openssl rand -base64 32>

# Your existing OIDC provider (Pocket ID, Authentik, Auth0, ...).
KRINT_OIDC_AUTHORITY=https://auth.example.com/realms/krint
KRINT_OIDC_CLIENT_ID=krint
KRINT_OIDC_REDIRECT_URI=http://localhost:5000/
KRINT_CORS_ORIGIN=http://localhost:5000
```

The image already runs as root, so no `user: root` override is needed in compose. The bind-mounted `./db` and `./backups` directories are created on first start.

### Editing the cloned compose (path 1)

If you went with the bundled-Keycloak clone path and now want to switch to your own IdP, edit the shipped `compose.yml`:

1. **Delete the `keycloak` service** and its `volumes:` `keycloak-data:` entry.
2. From the `krint` service, **remove** `Oidc__InternalAuthority` and the `depends_on: keycloak` line.
3. Drop the keycloak `depends_on` entirely if KRINT only depends on `db`.

Edit `.env`:

1. **Delete every `KC_*` variable**: they only configure the bundled Keycloak.
2. Point KRINT at your external IdP:

```env
# Public discovery URL of YOUR realm/tenant
Oidc__Authority=https://auth.example.com/application/o/krint/
Oidc__ClientId=<the client id from your IdP>

# Where the IdP sends users after login (must match what the IdP has registered)
Oidc__RedirectUri=http://localhost:5000/
Oidc__PostLogoutRedirectUri=http://localhost:5000/

Oidc__Scope=openid profile email
Oidc__RequireHttpsMetadata=true     # set to false only if your IdP is HTTP

Cors__AllowedOrigins__0=http://localhost:5000
```

`Oidc__Authority` must end **exactly** where the IdP's discovery document advertises `issuer`. Trailing slash matters. If discovery returns `"issuer": "https://auth.example.com/application/o/krint"` (no slash) then your `Oidc__Authority` must not have one either, otherwise token validation fails with `iss invalid`.

### Provider quirks

- **Pocket ID**: Toggle the client to **Public Client** (so it uses PKCE) and add your user/group to **Erlaubte Benutzergruppen** (or set to Unrestricted). Redirect URI supports `*` wildcards.
- **Authentik**: Create a Provider (OAuth2/OpenID), then an Application bound to it. Use the *Provider*'s issuer URL (`/application/o/<slug>/`) as `Oidc__Authority`, not the Application URL.
- **Auth0**: `Oidc__Authority` is `https://<tenant>.auth0.com/` (trailing slash required). Set the Application type to **Single Page Application**. No `roles` scope by default, but KRINT works without it.
- **Plain Keycloak (external)**: Configure exactly like the bundled one but skip `Oidc__InternalAuthority`. `Oidc__Authority` is `https://<keycloak>/realms/<realm>`.

---

## Reverse proxy (optional)

If you're putting KRINT behind a reverse proxy (Caddy, Traefik, nginx), you need:

1. **Public origin** matches `Oidc__RedirectUri` and `Cors__AllowedOrigins__0`. Update both if your domain changes.
2. **Keycloak's `KC_HOSTNAME`** must be the full public Keycloak URL. Tokens carry this as `iss`, and the API's `Oidc__Authority` must match it exactly.
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
Oidc__Authority=https://sso.example.com/realms/krint
Oidc__RedirectUri=https://krint.example.com/
Oidc__PostLogoutRedirectUri=https://krint.example.com/
Oidc__RequireHttpsMetadata=true
Cors__AllowedOrigins__0=https://krint.example.com
```

`Oidc__InternalAuthority` in `compose.yml` stays as `http://keycloak:8080/realms/krint`. The API always reaches Keycloak via the compose network regardless of what the public URL looks like.

---

## Troubleshooting

| Symptom                                                                          | Likely cause / fix                                                                                                           |
| -------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `krint` exits with `Vault:MasterKey must decode to 32 bytes`                     | `Vault__MasterKey` is empty or not a base64-encoded 32-byte value. Regenerate with `openssl rand -base64 32`.                |
| API returns `401 invalid_token: issuer is invalid`                               | `Oidc__Authority` doesn't match the `issuer` claim in tokens. With the bundled Keycloak it must equal `KC_HOSTNAME` + `/realms/krint`; with an external IdP it must match the `issuer` field of `<authority>/.well-known/openid-configuration` **byte-for-byte** (scheme, port, trailing slash). |
| Login succeeds at the IdP but lands on "You're not allowed to access this service" | The IdP allows the user to authenticate but the client policy denies them. Pocket ID: add the user's group to **Erlaubte Benutzergruppen** or toggle Unrestricted. Authentik: check the Application's policy bindings. |
| API returns `401 invalid_token: signature key was not found`                     | `Oidc__InternalAuthority` is wrong or Keycloak's `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` is unset, so the API can't reach the JWKS endpoint. |
| CORS error in the browser console on `/realms/krint/protocol/openid-connect/token` | The Keycloak client's web origins don't include the SPA origin. Edit `keycloak/krint-realm.json` (or use the admin UI), add your origin to `webOrigins`, drop `krint_keycloak-data` volume, restart. |
| CORS error in the browser console on `/api/*`                                    | `Cors__AllowedOrigins__0` doesn't match the SPA origin (no trailing slash).                                                  |
| Create fails with `Cannot connect to the Docker daemon`                          | The Docker socket isn't mounted into `krint`. Verify the `/var/run/docker.sock` bind exists in the running container.        |
| Provision or upgrade fails with `Failed to connect to 127.0.0.1:<port>` after 60s | `krint` can't reach the host's published port via its own loopback. Make sure the krint service has `extra_hosts: ["host.docker.internal:host-gateway"]` in `compose.yml`. The shipped compose has it; older self-host compose files won't. |
| Dashboard 500 with `SocketException (13): Permission denied`                     | You're running the container as non-root on a host where the docker socket isn't readable by that UID/GID. The shipped image runs as root for this reason. If you've overridden `user:` in compose, add `group_add: ["<host-docker-gid>"]` or drop the override. |
| `No free host port in range` when provisioning                                   | `krint.yaml`'s `port_ranges` exhausted. Either delete a previous instance or expand the range and `docker compose restart krint`. |
| Keycloak admin works but the krint realm 404s                                    | Realm import was skipped because the data volume already existed. `docker compose down && docker volume rm krint_keycloak-data && docker compose up -d`. |
| DB auth fails on a re-run with a changed password                                | `POSTGRES_PASSWORD` is only honoured on **first init**. Either reset the password in-DB or wipe `postgres-data`.             |

---

## Going further

- **Backups**: schedule cron-based dumps from the Backups page (writes into `./backups/`).
- **Engines and plugins**: see the engine matrix in the SPA's Create wizard or the README's Supported Engines table.
- **Hacking on it**: see [`docs/dev-setup.md`](./dev-setup.md) for the local-development setup with bun + `dotnet run`.

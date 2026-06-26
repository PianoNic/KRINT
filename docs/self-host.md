# Self-host KRINT

Run KRINT from the pre-built image, on either registry:

- **GitHub Container Registry** - `ghcr.io/pianonic/krint:latest`
- **Docker Hub** - `pianonic/krint:latest`

The two are identical; use whichever you prefer. Examples below use the GHCR tag.

You need a Linux/Windows host with **Docker + Compose v2**, and a directory to keep state in.

## Pick your path

| You have… | Do this |
| --- | --- |
| Your own OIDC provider (Pocket ID, Authentik, Auth0, Keycloak…) | [Quickstart](#quickstart) below - two files, no clone. |
| Nothing yet, want zero-config auth | [Bundled Keycloak](#no-oidc-provider-bundled-keycloak) - clone the repo. |
| A single machine, just you | The [desktop app](./desktop.md) - SQLite, built-in login, no Docker auth setup. |

## Quickstart

Drop these two files in an empty folder and run `docker compose up -d`. Open <http://localhost:5000>. State lives in `./db/` and `./backups/`.

**`compose.yml`**

```yaml
services:
  krint:
    image: ghcr.io/pianonic/krint:latest   # or pianonic/krint:latest (Docker Hub)
    container_name: krint
    restart: unless-stopped
    extra_hosts:
      - "host.docker.internal:host-gateway"   # how krint reaches provisioned DBs
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
      - /var/run/docker.sock:/var/run/docker.sock   # Windows: //var/run/docker.sock
      - ./backups:/app/backups

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

**`.env`**

```env
POSTGRES_PASSWORD=change-me
KRINT_VAULT_KEY=GENERATE_ME          # openssl rand -base64 32

KRINT_OIDC_AUTHORITY=https://auth.example.com/realms/krint
KRINT_OIDC_CLIENT_ID=krint
KRINT_OIDC_REDIRECT_URI=http://localhost:5000/
KRINT_CORS_ORIGIN=http://localhost:5000
```

On your IdP, register KRINT as a **public client** (PKCE, no secret) with redirect URI `http://localhost:5000/*`. That's it - the rest is reference below.

## No OIDC provider? Bundled Keycloak

For zero-config auth, clone the repo. Its `compose.yml` ships Keycloak with a ready-to-import realm:

```bash
git clone https://github.com/PianoNic/KRINT.git && cd KRINT
cp .env.example .env     # edit before first start
docker compose up -d     # postgres + keycloak + krint
```

First boot imports the `krint` realm (~30-60s). Then open Keycloak at <http://localhost:8080>, log in as the bootstrap admin from `.env`, switch to the **krint** realm, and add a user under **Users → Add user**. Log in to KRINT at <http://localhost:5000>.

---

## Configuration reference

<details>
<summary><strong>Environment variables</strong></summary>

Set these on the `krint` service (the Quickstart pulls them from `.env`).

| Variable | What it does |
| --- | --- |
| `Vault__MasterKey` | AES-256 key for the secrets vault. **32 random bytes, base64** (`openssl rand -base64 32`). Encrypts every instance password. |
| `ConnectionStrings__KrintDatabase` | KRINT's own metadata DB. Postgres: `Host=db;Port=5432;Database=krint;Username=postgres;Password=…`. SQLite: `Data Source=/data/krint.db`. |
| `Database__Provider` | `Postgres` or `Sqlite` (default). Picks the metadata store. |
| `Oidc__Authority` | Public IdP discovery URL. Must match the `issuer` in `<authority>/.well-known/openid-configuration` **byte-for-byte** (scheme, port, trailing slash). |
| `Oidc__ClientId` | Client ID registered on the IdP (public/PKCE). |
| `Oidc__RedirectUri` / `…PostLogoutRedirectUri` | Return URL after login/logout. Must be registered on the IdP, keep the trailing slash. |
| `Oidc__Scope` | `openid profile email roles` (`roles` optional). |
| `Oidc__RequireHttpsMetadata` | `true` (set `false` only for a plain-HTTP IdP). |
| `Cors__AllowedOrigins__0` | Browser origin allowed to call the API - KRINT URL **without** trailing slash. Add more as `__1`, `__2`. |

> ⚠️ **Never lose or rotate `Vault__MasterKey` once you have data.** There's no recovery and no key-rotation flow.

</details>

<details>
<summary><strong>SQLite instead of Postgres</strong></summary>

Drop the `db` service and point KRINT at a file on a mounted volume:

```yaml
    environment:
      Database__Provider: "Sqlite"
      ConnectionStrings__KrintDatabase: "Data Source=/data/krint.db"
    volumes:
      - ./db:/data
```

Remove `depends_on: db`. The file **must** be on a volume or it's wiped on every recreate.

</details>

<details>
<summary><strong>Instance data on a host folder</strong></summary>

By default each provisioned instance gets a named Docker volume. To see the raw files instead, set in `krint.yaml`:

```yaml
krint:
  storage:
    mode: HostFolder
    host_path: /data/krint     # path on the Docker HOST
```

New provisions then bind-mount `${host_path}/${containerName}`. Two gotchas: engines run as a fixed UID and need write access (`chown 999:999 …` for Postgres), and delete-cleanup only works if `host_path` is mounted into the krint container at the same path.

</details>

<details>
<summary><strong>OIDC provider setup &amp; quirks</strong></summary>

KRINT's SPA uses Authorization Code Flow + **PKCE**, so register a **public client** (no secret). Configure on the IdP:

| Setting | Value |
| --- | --- |
| Client type | Public (PKCE) |
| Redirect / post-logout URI | `http://localhost:5000/*` (or your public URL) |
| Web origins / CORS | KRINT origin, no trailing slash |
| Scopes | `openid profile email` (`roles` optional) |

- **Pocket ID** - toggle **Public Client**; allow your user/group. `*` wildcards work.
- **Authentik** - use the *Provider*'s issuer (`/application/o/<slug>/`) as the authority, not the Application URL.
- **Auth0** - authority is `https://<tenant>.auth0.com/` (trailing slash); app type **SPA**.
- **External Keycloak** - authority is `https://<host>/realms/<realm>`.

</details>

<details>
<summary><strong>Reverse proxy (Caddy/Traefik/nginx)</strong></summary>

Make the public origin match `Oidc__RedirectUri` and `Cors__AllowedOrigins__0`, trust `X-Forwarded-*`, and serve over HTTPS.

```caddy
krint.example.com { reverse_proxy krint:8080 }
```

```env
Oidc__RedirectUri=https://krint.example.com/
Oidc__PostLogoutRedirectUri=https://krint.example.com/
Oidc__RequireHttpsMetadata=true
Cors__AllowedOrigins__0=https://krint.example.com
```

With the bundled Keycloak, also set `KC_HOSTNAME=https://sso.example.com` and the matching `Oidc__Authority`. `Oidc__InternalAuthority` stays the in-cluster URL.

</details>

---

## Operations

**Upgrade**

```bash
docker compose pull krint && docker compose up -d krint
```

Migrations run on startup; the vault, metadata, and provisioned containers are preserved. Pin a version by replacing `:latest` with a [published tag](https://github.com/PianoNic/KRINT/pkgs/container/krint).

**Back up** the `./db/` (or `postgres-data` volume) and `./backups/` directories before major upgrades. Provisioned-instance volumes are independent and survive `docker compose down`.

**Schedule dumps** from the Backups page - they land in `./backups/`.

---

## Troubleshooting

<details>
<summary><strong>Common errors &amp; fixes</strong></summary>

| Symptom | Fix |
| --- | --- |
| `Vault:MasterKey must decode to 32 bytes` | Regenerate with `openssl rand -base64 32`. |
| `401 invalid_token: issuer is invalid` | `Oidc__Authority` must match the IdP's `issuer` byte-for-byte. |
| `401: signature key was not found` | API can't reach JWKS - check `Oidc__InternalAuthority` / `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`. |
| "You're not allowed to access this service" | IdP authenticated the user but the client policy denies them - allow the user/group. |
| CORS error on `/api/*` | `Cors__AllowedOrigins__0` must match the SPA origin (no trailing slash). |
| `Cannot connect to the Docker daemon` | The `/var/run/docker.sock` bind is missing from the `krint` service. |
| `Failed to connect to 127.0.0.1:<port>` on provision | Add `extra_hosts: ["host.docker.internal:host-gateway"]`. |
| `SocketException (13): Permission denied` | Running as non-root without socket access - keep the default root user or `group_add` the docker GID. |
| `No free host port in range` | `krint.yaml` `port_ranges` exhausted - delete an instance or widen the range. |
| DB auth fails after changing the password | `POSTGRES_PASSWORD` only applies on first init - reset in-DB or wipe the volume. |

</details>

---

See also: [Declarative instances](./declarative-instances.md) · [Nodes](./nodes.md) · [Developer setup](./dev-setup.md)

# KRINT — Self-Host

Run KRINT against the pre-built image. This guide assumes:

- A Linux or Windows host with **Docker + Compose v2**
- A directory you can leave running (state lives in named Docker volumes and a `./backups` bind mount)
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

Everything is wired in the shipped `compose.yml` — you don't need to add or override services. A single `.env` configures the stack.

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

Copy `.env.example` to `.env` and fill the values. Variables use the same names the containers actually read (no compose-level rewriting), so anything you set here lands directly in the relevant container's environment.

### Keycloak

| Variable                       | What it is                                                                                                |
| ------------------------------ | --------------------------------------------------------------------------------------------------------- |
| `KC_BOOTSTRAP_ADMIN_USERNAME`  | Initial Keycloak admin (master realm). Defaults to `admin`.                                               |
| `KC_BOOTSTRAP_ADMIN_PASSWORD`  | Initial Keycloak admin password. Change after first login.                                                |
| `KC_HOSTNAME`                  | **Full** public URL Keycloak should advertise (e.g. `http://localhost:8080`, or `https://sso.example.com`). Tokens carry this in `iss`. |
| `KC_HTTP_ENABLED`              | `true` for local HTTP. Keep `true` even behind a TLS-terminating proxy.                                   |
| `KC_PROXY_HEADERS`             | `xforwarded` — trusts `X-Forwarded-*` from a reverse proxy.                                               |
| `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` | `true` — lets the API container fetch Keycloak metadata via the compose-network URL while browsers still see the public URL. Required for OIDC to work end-to-end. |

> DB credentials (`POSTGRES_PASSWORD`, `KC_DB_USERNAME`, `KC_DB_PASSWORD`) are **not** in `.env` — they're hardcoded in `compose.yml` because they only matter inside the compose network. Edit `compose.yml` directly if you want to change them.

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

> **Don't lose `Vault__MasterKey`.** All provisioned-instance passwords are encrypted under this key. Lose it and you can't decrypt the vault — there is no recovery flow and **no key-rotation flow yet**, so don't rotate it once you have data.

### Set inside `compose.yml`, not `.env`

These live in the `krint` service's `environment:` block because they reference the compose network or include credentials that mirror `compose.yml`:

| Variable                            | Value                                                                                    |
| ----------------------------------- | ---------------------------------------------------------------------------------------- |
| `ConnectionStrings__KrintDatabase`  | `Host=db;Port=5432;Database=krint;Username=postgres;Password=<the-shared-pw>`            |
| `Oidc__InternalAuthority`           | `http://keycloak:8080/realms/krint` — the in-cluster Keycloak URL the API fetches discovery + JWKS from. Don't change unless you rename the keycloak service. |

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
2. **Wait for Keycloak.** First boot imports the `krint` realm and seeds the OIDC client — give it 30–60s. Tail with `docker compose logs -f keycloak` and wait for `Listening on: http://0.0.0.0:8080`.
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

To pin a version, replace `:latest` with a tag — see https://github.com/PianoNic/KRINT/pkgs/container/krint for the published tags (`0.1.0`, `0.1`, `0`, `latest`).

---

## Data persistence

| Location                | What it holds                                                                                                   |
| ----------------------- | --------------------------------------------------------------------------------------------------------------- |
| `postgres-data` volume  | KRINT's metadata DB + Keycloak's DB (users, sessions, realm config).                                            |
| `keycloak-data` volume  | Keycloak's writeable state (kept separately from the DB).                                                       |
| `./backups/` (bind)     | Dumps written by **Backups** for instances that support `pg_dump` / `mysqldump` / `mongodump` / Redis snapshot. Visible directly in your repo folder, gitignored. |
| *(per-instance volumes)*| Each provisioned engine gets its own auto-named volume (`krint-<engine>-<id>-data`).                            |

Back up `postgres-data`, `keycloak-data`, and `./backups/` before any major upgrade. Provisioned-instance volumes are independent and survive `docker compose down` of the KRINT stack.

---

## Reverse proxy (optional)

If you're putting KRINT behind a reverse proxy (Caddy, Traefik, nginx), you need:

1. **Public origin** matches `Oidc__RedirectUri` and `Cors__AllowedOrigins__0`. Update both if your domain changes.
2. **Keycloak's `KC_HOSTNAME`** must be the full public Keycloak URL — tokens carry this as `iss`, and the API's `Oidc__Authority` must match it exactly.
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

`Oidc__InternalAuthority` in `compose.yml` stays as `http://keycloak:8080/realms/krint` — the API always reaches Keycloak via the compose network regardless of what the public URL looks like.

---

## Troubleshooting

| Symptom                                                                          | Likely cause / fix                                                                                                           |
| -------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `krint` exits with `Vault:MasterKey must decode to 32 bytes`                     | `Vault__MasterKey` is empty or not a base64-encoded 32-byte value. Regenerate with `openssl rand -base64 32`.                |
| API returns `401 invalid_token: issuer is invalid`                               | `Oidc__Authority` doesn't equal `KC_HOSTNAME` + `/realms/krint`. They must be the same public URL byte-for-byte (incl. scheme + port). |
| API returns `401 invalid_token: signature key was not found`                     | `Oidc__InternalAuthority` is wrong or Keycloak's `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` is unset — API can't reach the JWKS endpoint. |
| CORS error in the browser console on `/realms/krint/protocol/openid-connect/token` | The Keycloak client's web origins don't include the SPA origin. Edit `keycloak/krint-realm.json` (or use the admin UI), add your origin to `webOrigins`, drop `krint_keycloak-data` volume, restart. |
| CORS error in the browser console on `/api/*`                                    | `Cors__AllowedOrigins__0` doesn't match the SPA origin (no trailing slash).                                                  |
| Create fails with `Cannot connect to the Docker daemon`                          | The Docker socket isn't mounted into `krint`. Verify the `/var/run/docker.sock` bind exists in the running container.        |
| `No free host port in range` when provisioning                                   | `krint.yaml`'s `port_ranges` exhausted. Either delete a previous instance or expand the range and `docker compose restart krint`. |
| Keycloak admin works but the krint realm 404s                                    | Realm import was skipped because the data volume already existed. `docker compose down && docker volume rm krint_keycloak-data && docker compose up -d`. |
| DB auth fails on a re-run with a changed password                                | `POSTGRES_PASSWORD` is only honoured on **first init**. Either reset the password in-DB or wipe `postgres-data`.             |

---

## Going further

- **Backups** — schedule cron-based dumps from the Backups page (writes into `./backups/`).
- **Engines and plugins** — see the engine matrix in the SPA's Create wizard or the README's Supported Engines table.
- **Hacking on it** — see [`docs/dev-setup.md`](./dev-setup.md) for the local-development setup with bun + `dotnet run`.

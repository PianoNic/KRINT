# Declarative instances (`instances.yaml`)

Infrastructure-as-code for your databases: declare instances in a YAML file and KRINT provisions
anything missing and reconciles passwords, databases, and users on startup — no clicking around the UI.

## Wiring it up

Point `krint.yaml` at an instances file (relative paths resolve against `krint.yaml`'s directory; absolute paths are taken verbatim):

```yaml
krint:
  storage:
    mode: Volume
  port_ranges:
    postgres: 30000-30099
    # ... (other engines)
  instances_file: instances.yaml
```

If `instances_file` is unset the reconcile hosted service no-ops and KRINT behaves exactly like before.

## Schema

```yaml
instances:
  - engine: postgres
    version: "18.4"
    display_name: prod-db
    default_database_name: app
    password: "Sup3r-Secret-Root.~"   # optional; auto-generated if omitted
    is_public: false                  # bind 127.0.0.1 (default) vs 0.0.0.0
    plugins:                          # engine-specific add-ons; see /supported endpoint
      - postgres-pg_stat_statements
    databases:
      - analytics
      - reporting
    users:
      - name: alice
        password: "alicepass-1.~"     # optional
        grant_databases:              # must reference databases above (or default_database_name)
          - app
          - analytics
      - name: bob
        grant_databases: [analytics]
```

Field rules:

- **`engine`, `version`, `display_name`** are required. `display_name` is the identity key - it's how the reconcile loop matches a config entry against an existing instance.
- **`password`** uses the SafePasswordGuard alphabet: `A-Z a-z 0-9 - _ . ~`. Other characters reject the whole entry.
- **`is_public: false`** binds the host port to `127.0.0.1` (only reachable from this host). `is_public: true` publishes on `0.0.0.0` so other machines on the LAN can reach it.
- **`plugins`** values are the engine plugin keys returned by `GET /api/Database/supported`.

## What reconcile does on startup

1. Loads `instances.yaml`. Refuses to run if any `display_name` is duplicated.
2. For each entry:
   - **New** (no row with that `display_name`): provisions a fresh instance, marks it config-managed.
   - **Existing**: marks it config-managed. Adds any missing databases. Adds any missing users. Re-applies user passwords whenever the spec sets them (cheap, idempotent). Rotates the root password if the spec value differs from what's in the vault.
3. **Orphans** - rows previously declared in config but absent from the file now - have their config-managed flag cleared. KRINT does NOT delete them; the user can clean them up via the UI.

Reconcile is **additive only**:
- Databases and users are never dropped automatically.
- The engine version isn't auto-upgraded if the config drifts.
- Container visibility (`is_public`) isn't auto-flipped after creation.

If you want to change one of those after the fact, edit it via the UI (after removing the entry from `instances.yaml` and restarting) or via the upgrade/visibility endpoints directly.

Errors per entry are logged at `Error` level and skipped. KRINT comes up healthy even when one block in the file is wrong.

## Frontend behavior for config-managed rows

Rows owned by `instances.yaml` are read-only in the UI:

- The **"Config"** badge appears next to the display name.
- Rename, upgrade, visibility, start/stop, delete, root-password rotation, create/drop databases & users, password resets, and grants are all disabled with a tooltip explaining why.
- Browsing tables, running queries, viewing logs, and using the exec terminal still work - those don't change the *declared* config.

To regain manual control of a config-managed instance:

1. Remove its entry from `instances.yaml`.
2. Restart KRINT.
3. The reconcile loop clears the flag, and the UI controls re-enable.

## Exporting an existing instance to YAML

The details dialog has a **Copy as YAML** action that returns a snippet matching the schema above, ready to paste into `instances.yaml`. Inner user passwords aren't exported (KRINT doesn't store them) - the snippet emits a comment so you can fill them in or let the next reconcile auto-generate them.

## Example

```yaml
instances:
  - engine: postgres
    version: "18.4"
    display_name: shop-prod
    default_database_name: shop
    password: "ZsRJpEMFcvWlGc1grfWbdfwUUTzv1Tum"
    is_public: false
    databases:
      - analytics
    users:
      - name: app
        password: "Hf3kBLm9XzN.~"
        grant_databases: [shop, analytics]

  - engine: mongo
    version: "7"
    display_name: events
    default_database_name: events
```

Boot KRINT once and you'll have a Postgres at `127.0.0.1:300xx` with an `app` user already granted to `shop` and `analytics`, plus a Mongo with a fresh admin password the reconcile generated on first run (visible from the instance details dialog).

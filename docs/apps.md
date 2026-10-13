# Use a database from your app

Every KRINT instance comes with a **connection string for your apps**. Paste it into your app's
compose file and you are done: no shared Docker network, no `extra_hosts`, no published ports.

```yaml
services:
  shop:
    image: my-shop
    environment:
      DATABASE_URL: postgres://shop:s3cret@host.docker.internal:30000/shop
```

Nothing is reachable from outside the machine. The database only listens on the host itself, and
your app's container reaches it through Docker.

## Give an app its own database

1. Open **Instances**, click the row menu of an instance and choose **New app database**.
2. Enter the app's name, for example `shop`.
3. Copy the connection string.

KRINT creates a database called `shop` and, where the engine has users, a user `shop` that can only
reach that database. The password is shown once. If you lose it, reset the user's password from
**Edit**.

One instance can host many apps this way. Each gets its own database and credentials.

| Engine | What "New app database" creates |
| --- | --- |
| PostgreSQL, TimescaleDB, MySQL, MariaDB, SQL Server, MongoDB, ClickHouse, CockroachDB | A database plus a user scoped to it |
| Cassandra, CouchDB, SeaweedFS, Azurite | A keyspace / database / bucket / container, using the instance credentials |
| Redis, Valkey, Qdrant, Neo4j | Not offered: use the instance's own connection string from **View** |

## Keep the connection string stable

Each instance gets a host port from its engine's range. To pin it, open **Advanced** in the create wizard and set **Fixed port** (or set `port:` in [instances.yaml](/declarative-instances)). The connection string then stays the same for good, even if you recreate the instance from YAML.

## Where the connection string points

| Docker host | Host in the connection string |
| --- | --- |
| Docker Desktop (Windows, macOS) | `host.docker.internal` |
| Native Linux | The default bridge gateway, usually `172.17.0.1` |

KRINT detects this per Docker host, including on [nodes](/nodes), and builds the string for you.
On Linux, KRINT binds each instance to `127.0.0.1` and to the bridge gateway. The bridge gateway
only exists inside the host, so other containers on the machine can connect while the LAN cannot.

Instances created before this feature only get the bridge binding once their container is
recreated. Upgrading the instance does that, and the details dialog says when it is needed.

::: tip Host firewall
On Linux hosts with a strict firewall (for example `ufw` with a default deny policy), traffic from
containers to the bridge gateway may be dropped. Allow the KRINT port ranges from the Docker
subnets if the connection times out.
:::

## Exposing a database to other machines

Apps on the same machine never need this. If another machine must reach a database directly, open
the instance's row menu and choose **Expose on LAN**. This publishes the port on all interfaces,
so anyone on your network can reach it; use strong passwords and keep it off when you can.

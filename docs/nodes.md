# Nodes

A **node** runs database containers on a remote host. Provision a database onto a node and then do everything with it through the control plane - browse, query, manage users, back up, upgrade, tail logs, and open a shell. Every operation rides a single SignalR connection, and nodes expose no ports.

::: warning
Nodes are experimental.
:::

A node is the *same* KRINT image started in a stripped role. It dials **out** to the control plane (so it works behind NAT and firewalls) and authenticates with a pre-shared token. The control plane keeps all state; the node holds nothing - it just runs the commands it's sent against its own loopback and returns the result.

Connecting a node has 3 steps:

1. **Set the control plane's public URL** so the generated compose can point back at it.
2. **Add the node** (UI or config) to mint a token and save it.
3. **Deploy the node** with that token. It dials in and shows up online.

## How it works

- **Control plane** (default role): the full KRINT app - UI, API, database, vault. It exposes the node hub at `/hubs/node` and authorizes nodes by their token.
- **Node** (`Krint__Role=node`): no UI, no app database, no auth. It bundles the Docker client and the database drivers, connects to the control plane, registers (name, machine, OS, Docker version), and stays connected - executing container lifecycle, queries, dumps/restores, log follows, and interactive shells against its own daemon. Only a `/health` endpoint is served.

A connected node appears on the control plane's **Nodes** page, where you can see its details and **Ping** it to confirm the channel is live. The node pings back every 5 seconds to keep its *last seen* current.

## Set the public URL

Set the public URL this control plane is served on in your **env file** so the Add-node compose is filled in automatically:

```env
Krint__PublicUrl=https://krint.example.com
```

::: info
If `Krint__PublicUrl` is unset, the Add-node modal still works but uses a placeholder URL you'll need to edit by hand.
:::

## Add a node

On the **Nodes** page, press **Add node**. KRINT generates a token and a ready-to-deploy compose with the control-plane URL baked in.

1. Edit the node name if you like (optional).
2. Copy the compose.
3. Press **Add node** to save it. Nothing is stored until you do.
4. Deploy the compose on the target host (see [Running a node](#running-a-node)).

The node shows as **pending** until it first connects, then **online**.

::: tip
The node's identity is derived from its token, so the generated compose needs no `Node__Id`.
:::

## Declare nodes in config

Nodes can also be declared up front in `krint.yaml`, just like instances - each is a name and a secret. On startup KRINT ensures a matching node exists (the secret is stored hashed):

```yaml
krint:
  nodes:
    - name: node-eu-1
      secret: a-long-random-shared-secret
    - name: node-us-1
      secret: another-long-random-secret
```

Deploy each node with its secret as `Node__Token`. Removing a node from the config stops re-asserting it; delete it from the UI to revoke access.

## Running a node

Set these on the node process (environment variables; `__` maps to nested config):

| Variable | Description | Default |
| --- | --- | --- |
| `Krint__Role` | Set to `node` to boot in node role. | `control plane` |
| `Node__ControlPlaneUrl` | Base URL of the control plane, e.g. `https://krint.example.com`. | — |
| `Node__Token` | The token from the Add-node modal, or the secret you declared in `krint.yaml`. | — |
| `Node__Name` | Display name. Ignored if the node was named in the UI or config. | machine name |

```yaml
services:
  krint-node:
    image: ghcr.io/pianonic/krint:latest   # or pianonic/krint:latest (Docker Hub)
    container_name: krint-node
    restart: unless-stopped
    environment:
      Krint__Role: "node"
      Node__ControlPlaneUrl: "https://krint.example.com"
      Node__Token: "a-long-random-shared-secret"
      Node__Name: "node-eu-1"
    volumes:
      # The node drives this host's Docker daemon.
      - /var/run/docker.sock:/var/run/docker.sock
```

The node retries on its own if the control plane is unreachable at boot, and re-registers automatically after a reconnect.

::: info
**Legacy:** a static `Node__Tokens` allow-list on the control plane still works; those nodes self-report a `Node__Id` (a stable GUID) instead of deriving their identity from the token.
:::

## Provision onto a node

Pick the node from the create wizard's **Target node** dropdown (it appears once a node is online), or pass `nodeId` in the provision request. The instance then shows a node badge on the instances list.

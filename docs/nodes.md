# Nodes (experimental)

A **node** runs database containers on a remote host. Provision a database onto it, then do
**everything** through the control plane - browse, query, manage users, back up, upgrade, tail logs,
open a shell. Every operation rides one SignalR connection, and nodes expose no ports.

A node is the *same* KRINT image in a stripped role. It dials **out** to the control plane (so it
works behind NAT/firewalls) and authenticates with a pre-shared token. The control plane keeps all
state; the node holds nothing - it just runs the commands it's sent against its own loopback and
returns the result.

## How it works

- **Control plane** (default role): the full KRINT app - UI, API, database, the works. It keeps an
  allow-list of node tokens (`Node__Tokens`) and exposes the node hub at `/hubs/node`.
- **Node** (`Krint__Role=node`): no UI, no app database, no auth. It bundles the Docker client and the
  database drivers and runs an agent that connects to the control plane, registers (name, machine, OS,
  Docker version) and stays connected, executing whatever the control plane sends - container
  lifecycle, SQL/queries, dumps/restores, log follows and interactive shells - against its own daemon.
  Only a `/health` endpoint is served.

A connected node appears on the control plane's **Nodes** page, where you can see its details and
**Ping** it to confirm the channel is live.

## Add a node (recommended)

The easiest way: on the **Nodes** page press **Add node**. KRINT shows a ready-to-deploy compose with
a freshly generated token and the control-plane URL baked in. Edit the node name if you like, then
**Add node** to save it (nothing is stored until you do). Deploy the compose on the target host and
it dials in - showing as *pending* until it first connects, then *online*.

For the URL to be filled in automatically, set the public URL this control plane is served on in the
**env file**:

```env
Krint__PublicUrl=https://krint.example.com
```

The node's identity is derived from its token, so the generated compose needs no `Node__Id`.

## Declare nodes in config

Nodes can also be declared up front in `krint.yaml`, just like instances - each is a name and a
secret. On startup KRINT ensures a matching node exists (token stored hashed):

```yaml
krint:
  nodes:
    - name: node-eu-1
      secret: a-long-random-shared-secret
    - name: node-us-1
      secret: another-long-random-secret
```

Deploy each node with its secret as `Node__Token` (see below). Removing a node from the config stops
re-asserting it; delete it from the UI to revoke access.

## Running a node

Set these on the node process (environment variables; `__` maps to nested config):

| Setting | Purpose |
| --- | --- |
| `Krint__Role=node` | Boot in node role. |
| `Node__ControlPlaneUrl` | Base URL of the control plane, e.g. `https://krint.example.com`. |
| `Node__Token` | The token from the Add-node modal or the secret you declared in `krint.yaml`. |
| `Node__Name` | Optional display name (defaults to the machine name). Ignored if the node was named in the UI/config. |

> Legacy: a static `Node__Tokens` allow-list on the control plane still works; those nodes self-report
> a `Node__Id` (a stable GUID) instead of having their identity derived from the token.

Provision onto a node from the create wizard's **Target node** dropdown (it appears once a node is
online), or pass `nodeId` in the provision request. The instance then shows a node badge on the
instances list.

### Compose snippet (node)

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

The node pings the control plane every 5 seconds so its *last seen* stays current, retries on its own
if the control plane is unreachable at boot, and re-registers automatically after a reconnect.

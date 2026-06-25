# Nodes (experimental)

> A node is a stateless worker that runs database containers on a remote host. You can provision a
> database onto a node and then do **everything** with it through the control plane — browse, query,
> manage users, back up and restore, upgrade the engine version, tail container logs, and open an
> interactive shell. **Every operation travels over the one SignalR connection**, and nodes expose no
> ports.

KRINT normally provisions database containers on its own Docker daemon. A **node** is the *same*
KRINT image started in a stripped role that does nothing but execute Docker (and database) work on
its own host. It dials **out** to the control plane over SignalR (so it works behind NAT/firewalls —
only the control plane needs to be reachable) and authenticates with a pre-shared token.

The control plane keeps **all** state (the single KRINT database and the secrets vault); the node
holds nothing. For a node-hosted instance, the control plane sends commands ("create this container",
"list these tables", "run this query") and the node runs them locally — against the container on its
own loopback — and returns the result. Nothing connects to a node directly, so the node's containers
bind to localhost only and never publish a port.

## How it works

- **Control plane** (default role): the full KRINT app — UI, API, database, the works. It keeps an
  allow-list of node tokens (`Node__Tokens`) and exposes the node hub at `/hubs/node`.
- **Node** (`Krint__Role=node`): no UI, no app database, no auth. It bundles the Docker client and the
  database drivers and runs an agent that connects to the control plane, registers (name, machine, OS,
  Docker version) and stays connected, executing whatever the control plane sends — container
  lifecycle, SQL/queries, dumps/restores, log follows and interactive shells — against its own daemon.
  Only a `/health` endpoint is served.

A connected node appears on the control plane's **Nodes** page, where you can see its details and
**Ping** it to confirm the channel is live.

## Running a node

Set these on the node process (environment variables; `__` maps to nested config):

| Setting | Purpose |
| --- | --- |
| `Krint__Role=node` | Boot in node role. |
| `Node__ControlPlaneUrl` | Base URL of the control plane, e.g. `https://krint.example.com`. |
| `Node__Token` | A token present in the control plane's `Node__Tokens` allow-list. |
| `Node__Id` | A stable GUID identifying this node. Pin it so provisioned instances keep pointing at the node across restarts; if unset, one is generated per run (and logged). |
| `Node__Name` | Display name for the node (defaults to the machine name). |

Provision onto a node from the create wizard's **Target node** dropdown (it appears once a node is
online), or pass `nodeId` in the provision request. The instance then shows a node badge on the
instances list.

On the **control plane**, list the accepted tokens:

```
Node__Tokens__0=a-long-random-shared-secret
Node__Tokens__1=another-node-secret
```

### Compose snippet (node)

```yaml
services:
  krint-node:
    image: ghcr.io/pianonic/krint:latest
    container_name: krint-node
    restart: unless-stopped
    environment:
      Krint__Role: "node"
      Node__ControlPlaneUrl: "https://krint.example.com"
      Node__Token: "a-long-random-shared-secret"
      Node__Id: "11111111-1111-1111-1111-111111111111"
      Node__Name: "node-eu-1"
    volumes:
      # The node drives this host's Docker daemon.
      - /var/run/docker.sock:/var/run/docker.sock
```

The node retries on its own if the control plane is unreachable at boot, and re-registers
automatically after a reconnect.

# Nodes (experimental)

> Phase 1 — connectivity foundation. A node connects, registers and answers pings, and shows up
> on the **Nodes** page. Routing actual database provisioning to a node is not wired up yet.

KRINT normally provisions database containers on its own Docker daemon. A **node** is the *same*
KRINT image started in a stripped role that does nothing but execute Docker work on its own host. It
dials **out** to the control plane over SignalR (so it works behind NAT/firewalls — only the control
plane needs to be reachable) and authenticates with a pre-shared token.

## How it works

- **Control plane** (default role): the full KRINT app — UI, API, database, the works. It keeps an
  allow-list of node tokens (`Node__Tokens`) and exposes the node hub at `/hubs/node`.
- **Node** (`Krint__Role=node`): no UI, no app database, no auth — just the Docker client and an agent
  that connects to the control plane, registers (name, machine, OS, Docker version) and stays
  connected. Only a `/health` endpoint is served.

A connected node appears on the control plane's **Nodes** page, where you can see its details and
**Ping** it to confirm the channel is live.

## Running a node

Set these on the node process (environment variables; `__` maps to nested config):

| Setting | Purpose |
| --- | --- |
| `Krint__Role=node` | Boot in node role. |
| `Node__ControlPlaneUrl` | Base URL of the control plane, e.g. `https://krint.example.com`. |
| `Node__Token` | A token present in the control plane's `Node__Tokens` allow-list. |
| `Node__Name` | Display name for the node (defaults to the machine name). |

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
      Node__Name: "node-eu-1"
    volumes:
      # The node drives this host's Docker daemon.
      - /var/run/docker.sock:/var/run/docker.sock
```

The node retries on its own if the control plane is unreachable at boot, and re-registers
automatically after a reconnect.

---
layout: home

hero:
  name: KRINT
  text: Your database is ready.
  tagline: One click. One key. A self-hosted database-provisioning platform for 16 engines.
  image:
    src: /logo.svg
    alt: KRINT
  actions:
    - theme: brand
      text: Self-host KRINT
      link: /self-host
    - theme: alt
      text: Developer setup
      link: /dev-setup
    - theme: alt
      text: GitHub
      link: https://github.com/PianoNic/KRINT

features:
  - title: 16 engines, one click
    details: PostgreSQL, MariaDB, MongoDB, MySQL, SQL Server, CockroachDB, TimescaleDB, ClickHouse, Cassandra, CouchDB, Neo4j, Redis, Valkey, Qdrant, plus SeaweedFS (S3) and Azurite (Azure Blob).
  - title: Browse, query, manage
    details: Spreadsheet-style row editing, an ad-hoc SQL console, live log tailing and an interactive shell — all from the browser.
  - title: Backups & upgrades
    details: Manual or cron-scheduled backups, restore in place, and dump-restore-swap version upgrades that keep the host port.
  - title: Distributed nodes
    details: Run the same image as a stripped node that dials into the control plane over SignalR to add remote Docker hosts — every operation tunnels over one connection.
---

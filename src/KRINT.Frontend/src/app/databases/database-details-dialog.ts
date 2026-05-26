import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { DatabasesStore } from '../shared/stores/databases.store';
import { CopyButton } from '../shared/components/copy-button/copy-button';

type DialogContext = { id: string };

@Component({
  selector: 'app-database-details-dialog',
  imports: [
    DatePipe,
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    CopyButton,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Instance details</h3>
      <p hlmDialogDescription>
        Read-only view of the connection string and credentials.
      </p>
    </hlm-dialog-header>

    @if (store.loadingDetails()) {
      <p class="text-muted-foreground text-sm">Loading…</p>
    } @else if (store.details(); as d) {
      @if (!d.isManaged) {
        <div class="border-amber-500/30 bg-amber-500/5 rounded-md border p-3 text-sm">
          <p class="font-medium">This database is not managed by KRINT.</p>
          <p class="text-muted-foreground mt-1">
            @if (d.containerName) {
              It was adopted from an existing Docker container. KRINT can back up, exec, and
              tail logs, but <strong>upgrade is disabled</strong> — the image is pinned by your
              orchestrator (docker compose, etc.) and upgrade there. Delete is a "forget" — the
              container itself will not be removed.
            } @else {
              It was registered as a remote external connection. Upgrade, backup, and container
              controls are disabled — KRINT only handles browsing, querying, and user management
              against the remote engine.
            }
          </p>
        </div>
      }
      <dl class="grid grid-cols-[auto_1fr_auto] items-center gap-x-3 gap-y-2 text-sm">
        <dt class="text-muted-foreground">Engine</dt>
        <dd class="font-mono">{{ d.engine }} {{ d.version }}</dd>
        <span></span>

        @if (d.containerName) {
          <dt class="text-muted-foreground">Container</dt>
          <dd class="font-mono break-all">{{ d.containerName }}</dd>
          <app-copy-button [value]="d.containerName" />
        }

        <dt class="text-muted-foreground">Host</dt>
        <dd class="font-mono">{{ d.host }}</dd>
        <app-copy-button [value]="d.host" />

        <dt class="text-muted-foreground">Port</dt>
        <dd class="font-mono">{{ d.port }}</dd>
        <app-copy-button [value]="d.port.toString()" />

        <dt class="text-muted-foreground">Database</dt>
        <dd class="font-mono">{{ d.databaseName }}</dd>
        <app-copy-button [value]="d.databaseName" />

        <dt class="text-muted-foreground">User</dt>
        <dd class="font-mono">{{ d.username }}</dd>
        <app-copy-button [value]="d.username" />

        <dt class="text-muted-foreground">Password</dt>
        <dd class="font-mono break-all">{{ d.password }}</dd>
        <app-copy-button [value]="d.password" />

        <dt class="text-muted-foreground">Created</dt>
        <dd class="font-mono">{{ d.createdAt | date: 'yyyy-MM-dd HH:mm:ss' }}</dd>
        <span></span>
      </dl>

      <div class="flex flex-col gap-2">
        <span class="text-muted-foreground text-sm">Connection string</span>
        <div class="flex items-start gap-2">
          <code class="bg-muted flex-1 break-all rounded-md p-3 font-mono text-xs">{{ d.connectionString }}</code>
          <app-copy-button [value]="d.connectionString" />
        </div>
      </div>
    } @else if (store.error(); as err) {
      <p class="text-destructive text-sm">{{ err }}</p>
    }

    <div class="flex justify-end">
      <button hlmBtn variant="outline" (click)="close()">Close</button>
    </div>
  `,
})
export class DatabaseDetailsDialog {
  protected readonly store = inject(DatabasesStore);
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  private readonly ctx = injectBrnDialogContext<DialogContext>();

  constructor() {
    this.store.loadDetails(this.ctx.id);
    effect((onCleanup) => {
      onCleanup(() => this.store.clearDetails());
    });
  }

  protected close(): void {
    this.ref.close();
  }
}

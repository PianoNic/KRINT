import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { interval } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideNetwork, lucideRefreshCw, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { NodesService } from '../api/api/nodes.service';
import { NodeDto } from '../api/model/nodeDto';
import { AddNodeDialogService } from './add-node-dialog';

@Component({
  selector: 'app-nodes',
  imports: [ContentHeader, DatePipe, NgIcon, HlmBadgeImports, HlmButtonImports, HlmTableImports],
  providers: [provideIcons({ lucideNetwork, lucideRefreshCw, lucidePlus, lucideTrash2 })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="mx-4 flex items-center justify-between gap-2 border-b py-2">
        <h2 class="text-sm font-medium">Nodes</h2>
        <div class="flex items-center gap-2">
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            (click)="reload()"
            [disabled]="loading()"
          >
            <ng-icon name="lucideRefreshCw" size="14" />
            {{ loading() ? 'Loading…' : 'Refresh' }}
          </button>
          <button hlmBtn size="sm" type="button" (click)="addNode()">
            <ng-icon name="lucidePlus" size="14" />
            Add node
          </button>
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto px-4">
        @if (nodes().length === 0 && !loading()) {
          <div
            class="text-muted-foreground flex flex-col items-center gap-2 p-10 text-center text-sm"
          >
            <ng-icon name="lucideNetwork" size="28" class="opacity-60" />
            <p>No nodes yet.</p>
            <p class="max-w-md text-xs">
              Press <strong>Add node</strong> to get a ready-to-deploy compose with a connection URL
              and a fresh token, then run it on the host you want to add.
            </p>
          </div>
        } @else {
          <table hlmTable>
            <thead hlmTableHeader>
              <tr hlmTableRow>
                <th hlmTableHead>Name</th>
                <th hlmTableHead>Status</th>
                <th hlmTableHead>Machine</th>
                <th hlmTableHead>OS</th>
                <th hlmTableHead>Docker</th>
                <th hlmTableHead>Connected</th>
                <th hlmTableHead>Last seen</th>
                <th hlmTableHead class="text-right">Ping</th>
              </tr>
            </thead>
            <tbody hlmTableBody>
              @for (n of nodes(); track n.id) {
                <tr hlmTableRow>
                  <td hlmTableCell class="font-medium">
                    <span class="inline-flex items-center gap-2">
                      {{ n.name }}
                      @if (n.isConfigManaged) {
                        <span hlmBadge variant="outline" class="text-xs">config</span>
                      }
                    </span>
                  </td>
                  <td hlmTableCell>
                    @if (n.online) {
                      <span hlmBadge variant="default" class="text-xs">online</span>
                    } @else if (n.pending) {
                      <span hlmBadge variant="outline" class="text-xs">pending</span>
                    } @else {
                      <span hlmBadge variant="secondary" class="text-xs">offline</span>
                    }
                  </td>
                  <td hlmTableCell class="text-sm">{{ n.machineName }}</td>
                  <td hlmTableCell class="max-w-xs truncate text-xs" [title]="n.os">{{ n.os }}</td>
                  <td hlmTableCell class="font-mono text-xs">{{ n.dockerVersion }}</td>
                  <td hlmTableCell class="font-mono text-xs">
                    {{ n.firstSeenAt | date: 'HH:mm:ss' }}
                  </td>
                  <td hlmTableCell class="font-mono text-xs">
                    {{ n.lastSeenAt | date: 'HH:mm:ss' }}
                  </td>
                  <td hlmTableCell class="text-right">
                    <div class="flex items-center justify-end gap-2">
                      @if (pingResults()[n.id]; as result) {
                        <span class="text-muted-foreground text-xs">{{ result }}</span>
                      }
                      <button
                        hlmBtn
                        variant="outline"
                        size="sm"
                        type="button"
                        [disabled]="pinging()[n.id] || !n.online"
                        (click)="ping(n.id)"
                      >
                        {{ pinging()[n.id] ? '…' : 'Ping' }}
                      </button>
                      <button
                        hlmBtn
                        variant="ghost"
                        size="sm"
                        type="button"
                        [disabled]="n.isConfigManaged || deleting()[n.id]"
                        [title]="n.isConfigManaged ? 'Managed by krint.yaml' : 'Remove node'"
                        (click)="remove(n)"
                      >
                        <ng-icon name="lucideTrash2" size="14" />
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }

        @if (error(); as err) {
          <p class="text-destructive mt-3 px-4 text-sm">{{ err }}</p>
        }
      </div>
    </section>
  `,
})
export class Nodes {
  private readonly api = inject(NodesService);
  private readonly addNodeDialog = inject(AddNodeDialogService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly nodes = signal<ReadonlyArray<NodeDto>>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly pinging = signal<Record<string, boolean>>({});
  protected readonly pingResults = signal<Record<string, string>>({});
  protected readonly deleting = signal<Record<string, boolean>>({});

  constructor() {
    this.reload();
    // Nodes connect/drop in real time, so refresh on a light cadence to keep the list current.
    interval(5000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reload(true));
  }

  protected reload(silent = false): void {
    if (!silent) this.loading.set(true);
    this.error.set(null);
    this.api.apiNodesGet().subscribe({
      next: (nodes) => {
        this.nodes.set(nodes);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err instanceof Error ? err.message : 'Failed to load nodes');
        this.loading.set(false);
      },
    });
  }

  protected async addNode(): Promise<void> {
    const created = await this.addNodeDialog.open();
    if (created) this.reload(true);
  }

  protected remove(node: NodeDto): void {
    if (!confirm(`Remove node "${node.name}"? Its token will stop working.`)) return;
    this.deleting.update((d) => ({ ...d, [node.id]: true }));
    this.api.apiNodesIdDelete(node.id).subscribe({
      next: () => {
        this.deleting.update((d) => ({ ...d, [node.id]: false }));
        this.reload(true);
      },
      error: (err) => {
        this.error.set(err instanceof Error ? err.message : 'Failed to remove the node');
        this.deleting.update((d) => ({ ...d, [node.id]: false }));
      },
    });
  }

  protected ping(id: string): void {
    this.pinging.update((p) => ({ ...p, [id]: true }));
    this.api.apiNodesIdPingPost(id).subscribe({
      next: (result) => {
        // roundTripMs is an integer the generator types as an opaque interface (.NET emits every
        // numeric as integer|string); coerce it for display.
        const ms = Number(result.roundTripMs as unknown);
        this.pingResults.update((r) => ({ ...r, [id]: `${result.reply} · ${ms}ms` }));
        this.pinging.update((p) => ({ ...p, [id]: false }));
      },
      error: () => {
        this.pingResults.update((r) => ({ ...r, [id]: 'unreachable' }));
        this.pinging.update((p) => ({ ...p, [id]: false }));
      },
    });
  }
}

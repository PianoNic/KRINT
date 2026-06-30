import { ChangeDetectionStrategy, Component, computed, inject, Injectable, signal } from '@angular/core';
import { BrnDialogRef } from '@spartan-ng/brain/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCopy, lucideCheck, lucideRefreshCw } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogService, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { NodesService } from '../api/api/nodes.service';
import { NodeDto } from '../api/model/nodeDto';

@Component({
  selector: 'app-add-node-dialog',
  imports: [NgIcon, HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription, HlmInputImports, HlmLabelImports],
  providers: [provideIcons({ lucideCopy, lucideCheck, lucideRefreshCw })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Add a node</h3>
      <p hlmDialogDescription>
        Deploy this compose on the host you want to add. Nothing is saved until you press <strong>Add node</strong>.
      </p>
    </hlm-dialog-header>

    <div class="flex flex-col gap-2">
      <label hlmLabel for="node-name">Node name <span class="text-muted-foreground">(optional)</span></label>
      <input hlmInput id="node-name" [value]="name()" (input)="onName($event)" placeholder="node-1" />
    </div>

    <div class="flex flex-col gap-2">
      <div class="flex items-center justify-between">
        <label hlmLabel>Compose</label>
        <div class="flex gap-2">
          <button hlmBtn variant="outline" size="sm" type="button" (click)="regenerate()" [disabled]="loading()">
            <ng-icon name="lucideRefreshCw" size="14" />
            New token
          </button>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="copy()">
            <ng-icon [name]="copied() ? 'lucideCheck' : 'lucideCopy'" size="14" />
            {{ copied() ? 'Copied' : 'Copy' }}
          </button>
        </div>
      </div>
      <pre class="bg-muted max-h-72 overflow-auto rounded-md border p-3 font-mono text-xs leading-relaxed">{{ compose() }}</pre>
    </div>

    @if (error(); as err) {
      <p class="text-destructive text-sm">{{ err }}</p>
    }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="ghost" type="button" (click)="cancel()" [disabled]="saving()">Cancel</button>
      <button hlmBtn type="button" (click)="save()" [disabled]="saving() || loading()">
        {{ saving() ? 'Adding…' : 'Add node' }}
      </button>
    </div>
  `,
})
export class AddNodeDialog {
  private readonly ref = inject(BrnDialogRef);
  private readonly api = inject(NodesService);

  protected readonly name = signal('');
  protected readonly token = signal('');
  protected readonly controlPlaneUrl = signal('');
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly copied = signal(false);

  // The compose mirrors docs/nodes.md. No Node__Id: the control plane derives the node's identity
  // from its token, so the node never needs to know its own id.
  protected readonly compose = computed(() => {
    const url = this.controlPlaneUrl();
    const name = this.name().trim() || 'node';
    return [
      'services:',
      '  krint-node:',
      '    image: ghcr.io/pianonic/krint:latest   # or pianonic/krint:latest (Docker Hub)',
      '    container_name: krint-node',
      '    restart: unless-stopped',
      '    environment:',
      '      Krint__Role: "node"',
      `      Node__ControlPlaneUrl: "${url}"`,
      `      Node__Token: "${this.token()}"`,
      `      Node__Name: "${name}"`,
      '    volumes:',
      '      - /var/run/docker.sock:/var/run/docker.sock',
      '',
    ].join('\n');
  });

  constructor() {
    this.loadDraft();
  }

  private loadDraft(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.apiNodesDraftGet().subscribe({
      next: (draft) => {
        if (!this.name()) this.name.set(draft.suggestedName);
        this.token.set(draft.token);
        this.controlPlaneUrl.set(draft.controlPlaneUrl);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err instanceof Error ? err.message : 'Failed to prepare a node');
        this.loading.set(false);
      },
    });
  }

  protected onName(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  protected regenerate(): void {
    this.loadDraft();
  }

  protected copy(): void {
    void navigator.clipboard?.writeText(this.compose()).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1500);
    });
  }

  protected save(): void {
    this.saving.set(true);
    this.error.set(null);
    this.api.apiNodesPost({ name: this.name().trim(), token: this.token() }).subscribe({
      next: (created) => this.ref.close(created),
      error: (err) => {
        this.error.set(err instanceof Error ? err.message : 'Failed to add the node');
        this.saving.set(false);
      },
    });
  }

  protected cancel(): void {
    this.ref.close(null);
  }
}

@Injectable({ providedIn: 'root' })
export class AddNodeDialogService {
  private readonly dialog = inject(HlmDialogService);

  open(): Promise<NodeDto | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(AddNodeDialog, { contentClass: 'sm:max-w-2xl' });
      ref.closed$.subscribe((result) => resolve((result as NodeDto | null) ?? null));
    });
  }
}

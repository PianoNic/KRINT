import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { DatabaseService } from '../api/api/database.service';
import { DatabasesStore } from '../shared/stores/databases.store';

type DialogContext = { id: string; engine: string; containerName: string; currentVersion: string };

@Component({
  selector: 'app-database-upgrade-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription, HlmLabelImports, HlmSelectImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Upgrade {{ ctx.containerName }}</h3>
      <p hlmDialogDescription>
        Dumps the current data, spins up a fresh {{ ctx.engine }} container at the picked
        version, restores into it, and swaps the connection over. The host port stays the
        same so your connection string keeps working. A backup is kept for rollback.
      </p>
    </hlm-dialog-header>

    <div class="flex flex-col gap-1.5">
      <label hlmLabel for="target-version" class="text-muted-foreground text-xs uppercase tracking-wide">
        Target version (currently <span class="font-mono">{{ ctx.currentVersion }}</span>)
      </label>
      <hlm-select [value]="targetVersion()" (valueChange)="targetVersion.set($event)" [disabled]="upgrading()">
        <hlm-select-trigger id="target-version" class="w-full">
          <hlm-select-value placeholder="Pick a version" />
        </hlm-select-trigger>
        <hlm-select-content *hlmSelectPortal>
          @for (v of availableVersions(); track v) {
            <hlm-select-item [value]="v">{{ v }}</hlm-select-item>
          }
        </hlm-select-content>
      </hlm-select>
    </div>

    @if (error(); as err) {
      <p class="text-destructive text-sm">{{ err }}</p>
    }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" [disabled]="upgrading()" (click)="close()">Cancel</button>
      <button hlmBtn type="button" [disabled]="!canSubmit()" (click)="submit()">
        {{ upgrading() ? 'Upgrading...' : 'Upgrade' }}
      </button>
    </div>
  `,
})
export class DatabaseUpgradeDialog {
  protected readonly store = inject(DatabasesStore);
  private readonly api = inject(DatabaseService);
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  protected readonly ctx = injectBrnDialogContext<DialogContext>();

  protected readonly targetVersion = signal<string | null>(null);
  protected readonly upgrading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly availableVersions = computed(() => {
    const spec = this.store.supported().find((s) => s.key === this.ctx.engine);
    return (spec?.versions ?? []).filter((v) => v !== this.ctx.currentVersion);
  });

  protected readonly canSubmit = computed(() => !this.upgrading() && this.targetVersion() !== null);

  protected submit(): void {
    const v = this.targetVersion();
    if (!v) return;
    this.upgrading.set(true);
    this.error.set(null);
    this.api.apiDatabaseIdUpgradePost(this.ctx.id, { targetVersion: v }).subscribe({
      next: () => {
        this.upgrading.set(false);
        this.store.load();
        this.ref.close();
      },
      error: (err: unknown) => {
        this.upgrading.set(false);
        this.error.set(messageOf(err));
      },
    });
  }

  protected close(): void {
    this.ref.close();
  }
}

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  if (err instanceof Error) return err.message;
  return 'Upgrade failed';
}

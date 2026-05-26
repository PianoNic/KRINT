import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { CopyButton } from '../shared/components/copy-button/copy-button';

type DialogContext = { yaml: string; displayName: string };

@Component({
  selector: 'app-database-export-yaml-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription, CopyButton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Export {{ ctx.displayName }} to YAML</h3>
      <p hlmDialogDescription>
        Paste this snippet into your <code>instances.yaml</code> file. KRINT will recreate or
        reconcile the instance on next startup. Inner user passwords aren't exported - see the
        inline comments.
      </p>
    </hlm-dialog-header>

    <div class="flex items-start gap-2">
      <pre class="bg-muted flex-1 max-h-[400px] overflow-auto rounded-md p-3 font-mono text-xs whitespace-pre">{{ ctx.yaml }}</pre>
      <app-copy-button [value]="ctx.yaml" />
    </div>

    <div class="flex justify-end">
      <button hlmBtn variant="outline" type="button" (click)="close()">Close</button>
    </div>
  `,
})
export class DatabaseExportYamlDialog {
  protected readonly ctx = injectBrnDialogContext<DialogContext>();
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);

  protected close(): void { this.ref.close(); }
}

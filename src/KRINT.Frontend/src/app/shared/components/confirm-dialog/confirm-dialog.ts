import { ChangeDetectionStrategy, Component, inject, Injectable } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogService, HlmDialogTitle } from '@spartan-ng/helm/dialog';

export type ConfirmDialogContext = {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
};

@Component({
  selector: 'app-confirm-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>{{ ctx.title }}</h3>
      <p hlmDialogDescription>{{ ctx.message }}</p>
    </hlm-dialog-header>

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" (click)="cancel()">
        {{ ctx.cancelLabel ?? 'Cancel' }}
      </button>
      <button
        hlmBtn
        type="button"
        [variant]="ctx.destructive ? 'destructive' : 'default'"
        (click)="confirm()"
      >
        {{ ctx.confirmLabel ?? 'Confirm' }}
      </button>
    </div>
  `,
})
export class ConfirmDialog {
  protected readonly ctx = injectBrnDialogContext<ConfirmDialogContext>();
  private readonly ref = inject(BrnDialogRef);

  protected confirm(): void { this.ref.close(true); }
  protected cancel(): void { this.ref.close(false); }
}

@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(HlmDialogService);

  open(ctx: ConfirmDialogContext): Promise<boolean> {
    return new Promise((resolve) => {
      // Alert-dialog semantics: don't dismiss on backdrop click - the user must
      // explicitly press Cancel or Confirm so destructive actions can't slip past.
      const ref = this.dialog.open(ConfirmDialog, {
        context: ctx,
        contentClass: 'sm:max-w-md',
        closeOnBackdropClick: false,
        closeOnOutsidePointerEvents: false,
      });
      ref.closed$.subscribe((result) => resolve(result === true));
    });
  }
}

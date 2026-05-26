import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { Injectable } from '@angular/core';

export type SetPasswordContext = {
  title: string;
  description: string;
  confirmLabel: string;
  destructive?: boolean;
};

/**
 * Tiny dialog that asks the user for an optional password. Leaving the input blank means
 * "auto-generate". On submit, the dialog closes with the (possibly empty) string; cancel
 * resolves null so the caller can no-op.
 *
 * Used by both the user reset flow and the instance root-password flow. The caller decides
 * what happens with the result.
 */
@Component({
  selector: 'app-set-password-dialog',
  imports: [
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmInputImports,
    HlmLabelImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>{{ ctx.title }}</h3>
      <p hlmDialogDescription>{{ ctx.description }}</p>
    </hlm-dialog-header>

    <div class="flex flex-col gap-2">
      <label hlmLabel for="set-password-input">
        New password <span class="text-muted-foreground">(blank = auto-generate)</span>
      </label>
      <input
        hlmInput
        id="set-password-input"
        type="password"
        autocomplete="new-password"
        placeholder="auto-generated"
        [value]="password()"
        (input)="password.set($any($event.target).value)"
        [attr.data-matches-spartan-invalid]="!!error()"
        [attr.aria-invalid]="!!error() || null"
      />
      @if (error(); as err) {
        <span class="text-destructive text-sm">{{ err }}</span>
      } @else {
        <span class="text-muted-foreground text-xs">Allowed characters: A-Z, a-z, 0-9, and - _ . ~</span>
      }
    </div>

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" (click)="cancel()">Cancel</button>
      <button hlmBtn type="button" [variant]="ctx.destructive ? 'destructive' : 'default'" (click)="submit()" [disabled]="!!error()">
        {{ ctx.confirmLabel }}
      </button>
    </div>
  `,
})
export class SetPasswordDialog {
  protected readonly ctx = injectBrnDialogContext<SetPasswordContext>();
  private readonly ref = inject<BrnDialogRef<string | null>>(BrnDialogRef);

  protected readonly password = signal('');

  // Matches SafePasswordGuard - empty value is fine (= auto-generate).
  private static readonly RE = /^[A-Za-z0-9\-_.~]+$/;
  protected readonly error = computed(() => {
    const v = this.password().trim();
    if (v === '') return null;
    return SetPasswordDialog.RE.test(v) ? null : 'Allowed characters: A-Z, a-z, 0-9, and - _ . ~';
  });

  protected submit(): void {
    if (this.error()) return;
    this.ref.close(this.password().trim()); // empty string means "auto-generate"
  }

  protected cancel(): void {
    this.ref.close(null);
  }
}

/**
 * Convenience wrapper: opens the dialog and returns a Promise that resolves to the entered
 * password (possibly empty) or null if the user cancelled. Mirrors the ConfirmService shape.
 */
@Injectable({ providedIn: 'root' })
export class SetPasswordService {
  private readonly dialog = inject(HlmDialogService);

  open(ctx: SetPasswordContext): Promise<string | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(SetPasswordDialog, {
        context: ctx,
        contentClass: 'sm:max-w-[420px]',
      });
      ref.closed$.subscribe((value: unknown) => resolve((value as string | null) ?? null));
    });
  }
}

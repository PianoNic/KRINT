import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideCopy, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

@Component({
  selector: 'app-copy-button',
  imports: [HlmButtonImports, HlmTooltipImports, NgIcon],
  providers: [provideIcons({ lucideCopy, lucideCheck, lucideTriangleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon"
      type="button"
      [attr.aria-label]="label()"
      [hlmTooltip]="label()"
      (click)="copy()"
    >
      <ng-icon [name]="copied() ? 'lucideCheck' : failed() ? 'lucideTriangleAlert' : 'lucideCopy'" size="14" />
    </button>
  `,
})
export class CopyButton {
  readonly value = input.required<string>();
  protected readonly copied = signal(false);
  protected readonly failed = signal(false);

  protected readonly label = computed(() =>
    this.copied() ? 'Copied' : this.failed() ? 'Copy failed - select the text and press Ctrl+C' : 'Copy',
  );

  protected async copy(): Promise<void> {
    // navigator.clipboard only exists on secure origins. A self-hosted KRINT reached over plain
    // http on a LAN is the normal case, so fall back to the legacy selection-based copy there.
    const ok = (await this.copyViaClipboardApi()) || this.copyViaSelection();
    this.copied.set(ok);
    this.failed.set(!ok);
    setTimeout(() => {
      this.copied.set(false);
      this.failed.set(false);
    }, 1500);
  }

  private async copyViaClipboardApi(): Promise<boolean> {
    if (!navigator.clipboard?.writeText) return false;
    try {
      await navigator.clipboard.writeText(this.value());
      return true;
    } catch {
      return false;
    }
  }

  private copyViaSelection(): boolean {
    const area = document.createElement('textarea');
    area.value = this.value();
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    try {
      return document.execCommand('copy');
    } catch {
      return false;
    } finally {
      document.body.removeChild(area);
    }
  }
}

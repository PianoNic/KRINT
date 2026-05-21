import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { DatabasesStore } from '../shared/stores/databases.store';
import { DatabaseDetailsDialog } from './database-details-dialog';

@Component({
  selector: 'app-databases',
  imports: [
    ContentHeader,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmTableImports,
  ],
  providers: [provideIcons({ lucideEye })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './databases.html',
})
export class Databases {
  protected readonly store = inject(DatabasesStore);
  private readonly dialog = inject(HlmDialogService);

  protected viewDetails(id: string): void {
    this.dialog.open(DatabaseDetailsDialog, {
      context: { id },
      contentClass: 'sm:max-w-[560px]',
    });
  }

  protected readonly engine = signal<string | null>(null);
  protected readonly version = signal<string | null>(null);

  protected readonly versions = computed(() => {
    const key = this.engine();
    if (!key) return [];
    return this.store.supported().find((s) => s.key === key)?.versions ?? [];
  });

  protected readonly canCreate = computed(
    () => !!this.engine() && !!this.version() && !this.store.creating(),
  );

  protected selectEngine(key: string | null): void {
    this.engine.set(key);
    this.version.set(null);
  }

  protected selectVersion(version: string | null): void {
    this.version.set(version);
  }

  protected readonly engineToLabel = (value: string): string =>
    this.store.supported().find((s) => s.key === value)?.displayName ?? value;

  protected create(): void {
    const engine = this.engine();
    const version = this.version();
    if (!engine || !version) return;
    this.store.create({ engine, version });
  }

  protected reload(): void {
    this.store.load();
  }
}

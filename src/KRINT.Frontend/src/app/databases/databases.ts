import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideDatabase, lucideEllipsisVertical, lucideEye, lucidePencil, lucideTrash2 } from '@ng-icons/lucide';
import { simpleMariadb, simpleMongodb, simpleMysql, simplePostgresql } from '@ng-icons/simple-icons';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { DatabaseInstanceDto } from '../api/model/databaseInstanceDto';
import { DatabasesStore } from '../shared/stores/databases.store';
import { DatabaseDetailsDialog } from './database-details-dialog';
import { DatabaseEditDialog } from './database-edit-dialog';

@Component({
  selector: 'app-databases',
  imports: [
    ContentHeader,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDropdownMenuImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmTableImports,
  ],
  providers: [
    provideIcons({
      lucideDatabase,
      lucideEllipsisVertical,
      lucideEye,
      lucidePencil,
      lucideTrash2,
      simplePostgresql,
      simpleMysql,
      simpleMongodb,
      simpleMariadb,
    }),
  ],
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

  protected editInstance(db: DatabaseInstanceDto): void {
    this.dialog.open(DatabaseEditDialog, {
      context: { id: db.id, engine: db.engine, containerName: db.containerName },
      contentClass: 'sm:max-w-[640px]',
    });
  }

  protected deleteInstance(db: DatabaseInstanceDto): void {
    if (!confirm(`Delete instance ${db.containerName}? This stops the container, removes its volume, and clears the secret.`)) return;
    this.store.deleteInstance(db.id);
  }

  protected engineIcon(engine: string): string {
    switch (engine) {
      case 'postgres': return 'simplePostgresql';
      case 'mysql':    return 'simpleMysql';
      case 'mongo':    return 'simpleMongodb';
      case 'mariadb':  return 'simpleMariadb';
      default:         return 'lucideDatabase';
    }
  }

  protected readonly engine = signal<string | null>(null);
  protected readonly version = signal<string | null>(null);
  protected readonly databaseName = signal('');

  protected readonly databaseNamePlaceholder = computed(() => {
    switch (this.engine()) {
      case 'postgres': return 'postgres';
      case 'mysql':    return 'mysql';
      case 'mongo':    return 'admin';
      default:         return 'default';
    }
  });

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
    const name = this.databaseName().trim();
    this.store.create({ engine, version, databaseName: name || null });
    this.databaseName.set('');
  }

  protected reload(): void {
    this.store.load();
  }
}

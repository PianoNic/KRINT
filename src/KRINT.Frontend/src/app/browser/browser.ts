import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight, lucideRefreshCw, lucideTable } from '@ng-icons/lucide';
import { simpleMariadb, simpleMongodb, simpleMysql, simplePostgresql } from '@ng-icons/simple-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { DatabaseService } from '../api/api/database.service';
import { DatabaseInstanceDto } from '../api/model/databaseInstanceDto';
import { TableSummaryDto } from '../api/model/tableSummaryDto';
import { TableRowsDto } from '../api/model/tableRowsDto';

@Component({
  selector: 'app-browser',
  imports: [
    ContentHeader,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmSelectImports,
    HlmTableImports,
  ],
  providers: [
    provideIcons({
      lucideChevronLeft,
      lucideChevronRight,
      lucideRefreshCw,
      lucideTable,
      simplePostgresql,
      simpleMysql,
      simpleMongodb,
      simpleMariadb,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './browser.html',
})
export class Browser {
  private readonly api = inject(DatabaseService);

  protected readonly instances = signal<ReadonlyArray<DatabaseInstanceDto>>([]);
  protected readonly databases = signal<ReadonlyArray<string>>([]);
  protected readonly tables = signal<ReadonlyArray<TableSummaryDto>>([]);
  protected readonly rows = signal<TableRowsDto | null>(null);

  protected readonly instanceId = signal<string | null>(null);
  protected readonly database = signal<string | null>(null);
  protected readonly table = signal<string | null>(null);

  protected readonly limit = signal(50);
  protected readonly offset = signal(0);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly selectedInstance = computed(() =>
    this.instances().find((i) => i.id === this.instanceId()) ?? null,
  );

  protected readonly hasPrev = computed(() => this.offset() > 0);
  protected readonly hasNext = computed(() => {
    const r = this.rows();
    if (!r) return false;
    const seenSoFar = this.offset() + r.rows.length;
    return r.totalCount != null ? seenSoFar < Number(r.totalCount) : r.rows.length === this.limit();
  });

  constructor() {
    this.api.apiDatabaseGet().subscribe({
      next: (list) => this.instances.set(list),
      error: (err) => this.error.set(messageOf(err)),
    });

    // When instance changes, refresh database list.
    effect(() => {
      const id = this.instanceId();
      if (!id) {
        this.databases.set([]);
        this.database.set(null);
        return;
      }
      this.loading.set(true);
      this.api.apiDatabaseIdDatabasesGet(id).subscribe({
        next: (dbs) => {
          this.databases.set(dbs);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(messageOf(err));
          this.loading.set(false);
        },
      });
    });

    // When database changes, list tables.
    effect(() => {
      const id = this.instanceId();
      const db = this.database();
      if (!id || !db) {
        this.tables.set([]);
        this.table.set(null);
        return;
      }
      this.loading.set(true);
      this.api.apiDatabaseIdBrowseDatabaseTablesGet(id, db).subscribe({
        next: (ts) => {
          this.tables.set(ts);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(messageOf(err));
          this.loading.set(false);
        },
      });
    });

    // When table or paging changes, fetch rows.
    effect(() => {
      const id = this.instanceId();
      const db = this.database();
      const tbl = this.table();
      const lim = this.limit();
      const off = this.offset();
      if (!id || !db || !tbl) {
        this.rows.set(null);
        return;
      }
      this.loading.set(true);
      this.api.apiDatabaseIdBrowseDatabaseTablesTableRowsGet(id, db, tbl, lim as any, off as any).subscribe({
        next: (r) => {
          this.rows.set(r);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(messageOf(err));
          this.loading.set(false);
        },
      });
    });
  }

  protected engineIcon(engine: string): string {
    switch (engine) {
      case 'postgres': return 'simplePostgresql';
      case 'mysql':    return 'simpleMysql';
      case 'mongo':    return 'simpleMongodb';
      case 'mariadb':  return 'simpleMariadb';
      default:         return 'simplePostgresql';
    }
  }

  protected selectInstance(id: string | null): void {
    this.instanceId.set(id);
    this.database.set(null);
    this.table.set(null);
    this.offset.set(0);
  }

  protected selectDatabase(name: string | null): void {
    this.database.set(name);
    this.table.set(null);
    this.offset.set(0);
  }

  protected selectTable(name: string): void {
    this.table.set(name);
    this.offset.set(0);
  }

  protected next(): void {
    this.offset.update((o) => o + this.limit());
  }

  protected prev(): void {
    this.offset.update((o) => Math.max(0, o - this.limit()));
  }

  protected truncate(value: string | null | undefined): string {
    if (value == null) return '∅';
    return value.length > 120 ? value.slice(0, 117) + '…' : value;
  }

  protected readonly instanceLabel = (id: string): string => {
    const i = this.instances().find((x) => x.id === id);
    return i ? `${i.engine} — ${i.containerName}` : id;
  };
}

function messageOf(err: unknown): string {
  if (err instanceof Error) return err.message;
  return 'Request failed';
}

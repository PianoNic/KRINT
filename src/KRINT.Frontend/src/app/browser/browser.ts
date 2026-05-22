import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCheck,
  lucideChevronLeft,
  lucideChevronRight,
  lucideEllipsisVertical,
  lucidePencil,
  lucidePlus,
  lucideRefreshCw,
  lucideTable,
  lucideTrash2,
  lucideX,
} from '@ng-icons/lucide';
import { simpleMariadb, simpleMongodb, simpleMysql, simplePostgresql } from '@ng-icons/simple-icons';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { DatabaseService } from '../api/api/database.service';
import { DatabaseInstanceDto } from '../api/model/databaseInstanceDto';
import { TableSummaryDto } from '../api/model/tableSummaryDto';
import { TableRowsDto } from '../api/model/tableRowsDto';

const NULL_TOKEN = Symbol('null');
type EditValue = string | typeof NULL_TOKEN;
type Draft = { values: EditValue[]; mode: 'edit' | 'insert'; rowIndex: number | null };

@Component({
  selector: 'app-browser',
  imports: [
    ContentHeader,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDropdownMenuImports,
    HlmInputImports,
    HlmSelectImports,
    HlmTableImports,
    HlmTooltipImports,
  ],
  providers: [
    provideIcons({
      lucideCheck,
      lucideChevronLeft,
      lucideChevronRight,
      lucideEllipsisVertical,
      lucidePencil,
      lucidePlus,
      lucideRefreshCw,
      lucideTable,
      lucideTrash2,
      lucideX,
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
  private readonly confirmService = inject(ConfirmService);
  protected readonly nullToken = NULL_TOKEN;

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

  protected readonly draft = signal<Draft | null>(null);
  protected readonly saving = signal(false);
  protected readonly deletingRowIndex = signal<number | null>(null);
  protected readonly editError = signal<string | null>(null);

  protected readonly selectedInstance = computed(() =>
    this.instances().find((i) => i.id === this.instanceId()) ?? null,
  );

  protected readonly canEdit = computed(() => {
    const engine = this.selectedInstance()?.engine;
    return engine === 'postgres' || engine === 'mysql' || engine === 'mariadb';
  });
  protected readonly canDropTable = computed(() => {
    // Drop works for all SQL engines + Mongo collections.
    return !!this.selectedInstance();
  });

  protected readonly editingIndex = computed(() => {
    const d = this.draft();
    return d?.mode === 'edit' ? d.rowIndex : null;
  });
  protected readonly inserting = computed(() => this.draft()?.mode === 'insert');

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

    effect(() => {
      const id = this.instanceId();
      if (!id) {
        this.databases.set([]);
        this.database.set(null);
        return;
      }
      this.loading.set(true);
      this.api.apiDatabaseIdDatabasesGet(id).subscribe({
        next: (dbs) => { this.databases.set(dbs); this.loading.set(false); },
        error: (err) => { this.error.set(messageOf(err)); this.loading.set(false); },
      });
    });

    effect(() => {
      const id = this.instanceId();
      const db = this.database();
      if (!id || !db) { this.tables.set([]); this.table.set(null); return; }
      this.refreshTables(id, db);
    });

    effect(() => {
      const id = this.instanceId();
      const db = this.database();
      const tbl = this.table();
      const lim = this.limit();
      const off = this.offset();
      if (!id || !db || !tbl) { this.rows.set(null); return; }
      this.cancelDraft();
      this.loadRows(id, db, tbl, lim, off);
    });
  }

  private refreshTables(id: string, db: string): void {
    this.loading.set(true);
    this.api.apiDatabaseIdBrowseDatabaseTablesGet(id, db).subscribe({
      next: (ts) => { this.tables.set(ts); this.loading.set(false); },
      error: (err) => { this.error.set(messageOf(err)); this.loading.set(false); },
    });
  }

  private loadRows(id: string, db: string, tbl: string, lim: number, off: number): void {
    this.loading.set(true);
    this.api.apiDatabaseIdBrowseDatabaseTablesTableRowsGet(id, db, tbl, lim as any, off as any).subscribe({
      next: (r) => { this.rows.set(r); this.loading.set(false); },
      error: (err) => { this.error.set(messageOf(err)); this.loading.set(false); },
    });
  }

  protected reloadRows(): void {
    const id = this.instanceId(); const db = this.database(); const tbl = this.table();
    if (id && db && tbl) this.loadRows(id, db, tbl, this.limit(), this.offset());
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

  protected next(): void { this.offset.update((o) => o + this.limit()); }
  protected prev(): void { this.offset.update((o) => Math.max(0, o - this.limit())); }

  protected truncate(value: string | null | undefined): string {
    if (value == null) return '∅';
    return value.length > 120 ? value.slice(0, 117) + '…' : value;
  }

  protected readonly instanceLabel = (id: string): string => {
    const i = this.instances().find((x) => x.id === id);
    return i ? `${i.engine} — ${i.containerName}` : id;
  };

  // ----- draft helpers -----
  protected beginEdit(rowIndex: number): void {
    const r = this.rows();
    if (!r) return;
    const values: EditValue[] = r.rows[rowIndex].map((v) => (v == null ? NULL_TOKEN : v));
    this.draft.set({ values, mode: 'edit', rowIndex });
    this.editError.set(null);
  }

  protected beginInsert(): void {
    const r = this.rows();
    if (!r) return;
    // Blank, empty-string per column. User toggles NULL with the ∅ button.
    const values: EditValue[] = r.columns.map(() => '');
    this.draft.set({ values, mode: 'insert', rowIndex: null });
    this.editError.set(null);
  }

  protected cancelDraft(): void {
    this.draft.set(null);
    this.editError.set(null);
  }

  protected updateDraft(colIndex: number, value: string): void {
    const d = this.draft(); if (!d) return;
    this.draft.set({ ...d, values: d.values.map((v, i) => (i === colIndex ? value : v)) });
  }

  protected toggleDraftNull(colIndex: number): void {
    const d = this.draft(); if (!d) return;
    this.draft.set({
      ...d,
      values: d.values.map((v, i) => (i === colIndex ? (v === NULL_TOKEN ? '' : NULL_TOKEN) : v)),
    });
  }

  protected isDraftNull(colIndex: number): boolean {
    return this.draft()?.values[colIndex] === NULL_TOKEN;
  }

  protected draftValue(colIndex: number): string {
    const v = this.draft()?.values[colIndex];
    return v === undefined || v === NULL_TOKEN ? '' : v;
  }

  protected saveDraft(): void {
    const d = this.draft(); const r = this.rows();
    const id = this.instanceId(); const db = this.database(); const tbl = this.table();
    if (!d || !r || !id || !db || !tbl) return;

    const newValues = d.values.map((v) => (v === NULL_TOKEN ? null : v));
    this.saving.set(true);
    this.editError.set(null);

    if (d.mode === 'insert') {
      this.api.apiDatabaseIdBrowseDatabaseTablesTableRowsPost(id, db, tbl, {
        columns: r.columns,
        values: newValues,
      } as any).subscribe({
        next: () => {
          this.cancelDraft();
          this.saving.set(false);
          this.reloadRows();
        },
        error: (err) => { this.editError.set(messageOf(err)); this.saving.set(false); },
      });
    } else {
      const rowIndex = d.rowIndex!;
      this.api.apiDatabaseIdBrowseDatabaseTablesTableRowsPatch(id, db, tbl, {
        columns: r.columns,
        originalValues: r.rows[rowIndex],
        newValues,
      } as any).subscribe({
        next: () => {
          this.rows.update((curr) =>
            curr === null
              ? curr
              : { ...curr, rows: curr.rows.map((row, i) => (i === rowIndex ? (newValues as unknown as string[]) : row)) },
          );
          this.cancelDraft();
          this.saving.set(false);
        },
        error: (err) => { this.editError.set(messageOf(err)); this.saving.set(false); },
      });
    }
  }

  protected async deleteRow(rowIndex: number): Promise<void> {
    const r = this.rows();
    const id = this.instanceId(); const db = this.database(); const tbl = this.table();
    if (!r || !id || !db || !tbl) return;
    const ok = await this.confirmService.open({
      title: `Delete row?`,
      message: `One row will be removed from "${tbl}". This cannot be undone.`,
      confirmLabel: 'Delete row',
      destructive: true,
    });
    if (!ok) return;

    this.deletingRowIndex.set(rowIndex);
    this.editError.set(null);
    this.api.apiDatabaseIdBrowseDatabaseTablesTableRowsDelete(id, db, tbl, {
      columns: r.columns,
      originalValues: r.rows[rowIndex],
    } as any).subscribe({
      next: () => {
        this.rows.update((curr) =>
          curr === null
            ? curr
            : { ...curr, rows: curr.rows.filter((_, i) => i !== rowIndex), totalCount: curr.totalCount != null ? (Number(curr.totalCount) - 1) as any : curr.totalCount },
        );
        this.deletingRowIndex.set(null);
      },
      error: (err) => { this.editError.set(messageOf(err)); this.deletingRowIndex.set(null); },
    });
  }

  protected async dropTable(name: string): Promise<void> {
    const id = this.instanceId(); const db = this.database();
    if (!id || !db) return;
    const ok = await this.confirmService.open({
      title: `Drop table "${name}"?`,
      message: 'Every row in it is permanently deleted. This cannot be undone.',
      confirmLabel: 'Drop table',
      destructive: true,
    });
    if (!ok) return;

    this.api.apiDatabaseIdBrowseDatabaseTablesTableDelete(id, db, name).subscribe({
      next: () => {
        if (this.table() === name) this.table.set(null);
        this.refreshTables(id, db);
      },
      error: (err) => this.error.set(messageOf(err)),
    });
  }
}

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  if (err instanceof Error) return err.message;
  return 'Request failed';
}

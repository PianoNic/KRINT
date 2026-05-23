import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBrain,
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
import { simpleApachecassandra, simpleApachecouchdb, simpleClickhouse, simpleCockroachlabs, simpleElasticsearch, simpleMariadb, simpleMongodb, simpleMysql, simpleNeo4j, simplePostgresql, simpleRedis, simpleTimescale } from '@ng-icons/simple-icons';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmContextMenuImports } from '@spartan-ng/helm/context-menu';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { QueryConsole } from '../shared/components/query-console/query-console';
import { customMssql, customQdrant, customValkey } from '../shared/icons/custom-icons';
import { DatabaseService } from '../api/api/database.service';
import { DatabaseInstanceDto } from '../api/model/databaseInstanceDto';
import { EngineCapabilitiesDto } from '../api/model/engineCapabilitiesDto';
import { SupportedDatabaseDto } from '../api/model/supportedDatabaseDto';
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
    HlmContextMenuImports,
    HlmDropdownMenuImports,
    HlmInputImports,
    HlmSelectImports,
    HlmTableImports,
    HlmTooltipImports,
    QueryConsole,
  ],
  providers: [
    provideIcons({
      lucideBrain,
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
      simpleApachecassandra,
      simpleApachecouchdb,
      simpleClickhouse,
      simpleCockroachlabs,
      simpleElasticsearch,
      simpleMariadb,
      simpleNeo4j,
      simpleRedis,
      simpleTimescale,
      customMssql,
      customQdrant,
      customValkey,
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
  protected readonly supported = signal<ReadonlyArray<SupportedDatabaseDto>>([]);
  protected readonly databases = signal<ReadonlyArray<string>>([]);
  protected readonly tables = signal<ReadonlyArray<TableSummaryDto>>([]);
  protected readonly rows = signal<TableRowsDto | null>(null);

  protected readonly instanceId = signal<string | null>(null);
  protected readonly database = signal<string | null>(null);
  protected readonly table = signal<string | null>(null);

  protected readonly limit = signal(50);
  protected readonly offset = signal(0);

  // Right-pane tabs. 'data' is the row browser (default); 'query' swaps in the SQL console
  // pre-scoped to the currently-selected instance + database.
  protected readonly view = signal<'data' | 'query'>('data');

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly draft = signal<Draft | null>(null);
  protected readonly saving = signal(false);
  protected readonly deletingRowIndex = signal<number | null>(null);
  protected readonly editError = signal<string | null>(null);

  // Spreadsheet-style in-place edits. Key is `${rowIdx}:${colIdx}`. Absence means
  // "use the server value at that cell". NULL_TOKEN means "user wants this cell set to NULL".
  // The map is cleared whenever the row set itself reloads (page change, table switch, refresh)
  // because the row indexes would no longer line up with what the user saw.
  protected readonly pendingEdits = signal<ReadonlyMap<string, EditValue>>(new Map());
  protected readonly hasChanges = computed(() => this.pendingEdits().size > 0);

  protected readonly selectedInstance = computed(() =>
    this.instances().find((i) => i.id === this.instanceId()) ?? null,
  );

  protected readonly capabilities = computed<EngineCapabilitiesDto | null>(() => {
    const engine = this.selectedInstance()?.engine;
    if (!engine) return null;
    return this.supported().find((s) => s.key === engine)?.capabilities ?? null;
  });

  // Convenience computed flags so the template stays readable.
  protected readonly canInsertRow = computed(() => this.capabilities()?.supportsRowInsert ?? false);
  protected readonly canEditRow   = computed(() => this.capabilities()?.supportsRowEdit ?? false);
  protected readonly canDeleteRow = computed(() => this.capabilities()?.supportsRowDelete ?? false);
  protected readonly canDropTable = computed(() => this.capabilities()?.supportsDropTable ?? false);
  protected readonly tableTerm    = computed(() => this.capabilities()?.tableTerm ?? 'table');
  protected readonly rowTerm      = computed(() => this.capabilities()?.rowTerm ?? 'row');
  protected readonly databaseTerm = computed(() => this.capabilities()?.databaseTerm ?? 'database');

  // Legacy alias kept so the template can keep using canEdit() until the next sweep.
  protected readonly canEdit = this.canEditRow;

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

    this.api.apiDatabaseSupportedGet().subscribe({
      next: (list) => this.supported.set(list),
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
        next: (dbs) => {
          this.databases.set(dbs);
          this.loading.set(false);
          // Preselect: prefer the instance's provisioned default DB; otherwise the only DB
          // present; otherwise leave the picker empty so the user makes the call.
          const instance = this.instances().find((i) => i.id === id);
          const preferred = instance && dbs.includes(instance.databaseName) ? instance.databaseName : null;
          this.database.set(preferred ?? (dbs.length === 1 ? dbs[0] : null));
        },
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
      this.pendingEdits.set(new Map());
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

  protected reloadInstances(): void {
    this.api.apiDatabaseGet().subscribe({
      next: (list) => this.instances.set(list),
      error: (err) => this.error.set(messageOf(err)),
    });
  }

  /** Refresh the whole left rail: instances, databases for the selected instance, and
   *  entities for the selected database. Used by the header refresh button so one click
   *  rehydrates everything visible. */
  protected refreshAll(): void {
    this.reloadInstances();
    const id = this.instanceId();
    if (!id) return;
    this.loading.set(true);
    this.api.apiDatabaseIdDatabasesGet(id).subscribe({
      next: (dbs) => { this.databases.set(dbs); this.loading.set(false); },
      error: (err) => { this.error.set(messageOf(err)); this.loading.set(false); },
    });
    const db = this.database();
    if (db) this.refreshTables(id, db);
  }

  protected instanceUrl(i: DatabaseInstanceDto): string {
    return `${i.host}:${i.port}/${i.databaseName}`;
  }

  protected engineIcon(engine: string): string {
    switch (engine) {
      case 'postgres': return 'simplePostgresql';
      case 'mysql':    return 'simpleMysql';
      case 'mongo':    return 'simpleMongodb';
      case 'mariadb':     return 'simpleMariadb';
      case 'timescaledb': return 'simpleTimescale';
      case 'redis':       return 'simpleRedis';
      case 'cockroachdb': return 'simpleCockroachlabs';
      case 'clickhouse':  return 'simpleClickhouse';
      case 'cassandra':   return 'simpleApachecassandra';
      case 'couchdb':     return 'simpleApachecouchdb';
      case 'elasticsearch': return 'simpleElasticsearch';
      case 'pgvector':    return 'simplePostgresql';
      case 'neo4j':       return 'simpleNeo4j';
      case 'qdrant':      return 'customQdrant';
      case 'valkey':      return 'customValkey';
      case 'mssql':       return 'customMssql';
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
    return i ? `${i.engine} - ${i.containerName}` : id;
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

  /** True for columns the DB owns: primary keys and generated/identity columns. The backend
   *  populates `columnInfos` for SQL-family engines (Postgres + MySQL families); for engines
   *  without metadata we fall back to a case-insensitive `id` match so the historical UX is
   *  preserved. The UI renders these as disabled inputs and the insert payload drops them. */
  protected isProtectedColumn(colIndex: number): boolean {
    const r = this.rows();
    const info = r?.columnInfos?.[colIndex];
    if (info) return info.isPrimaryKey || info.isGenerated;
    return r?.columns[colIndex]?.toLowerCase() === 'id';
  }

  protected draftValue(colIndex: number): string {
    const v = this.draft()?.values[colIndex];
    return v === undefined || v === NULL_TOKEN ? '' : v;
  }

  // ----- in-place cell editing -----
  private cellKey(rowIdx: number, colIdx: number): string { return `${rowIdx}:${colIdx}`; }

  private serverCell(rowIdx: number, colIdx: number): string | null {
    const v = this.rows()?.rows[rowIdx]?.[colIdx];
    return v == null ? null : v;
  }

  /** Current pending or server value for a cell. Returns NULL_TOKEN for NULL. */
  protected cellValue(rowIdx: number, colIdx: number): EditValue {
    const pending = this.pendingEdits().get(this.cellKey(rowIdx, colIdx));
    if (pending !== undefined) return pending;
    const server = this.serverCell(rowIdx, colIdx);
    return server === null ? NULL_TOKEN : server;
  }

  /** String to bind into the input's value. NULL renders as empty (placeholder shows "null"). */
  protected cellInputValue(rowIdx: number, colIdx: number): string {
    const v = this.cellValue(rowIdx, colIdx);
    return v === NULL_TOKEN ? '' : v;
  }

  /** Placeholder text in light gray for empty/null cells so the user can tell them apart. */
  protected cellPlaceholder(rowIdx: number, colIdx: number): string {
    const v = this.cellValue(rowIdx, colIdx);
    if (v === NULL_TOKEN) return 'null';
    if (v === '') return 'empty';
    return '';
  }

  protected isCellDirty(rowIdx: number, colIdx: number): boolean {
    return this.pendingEdits().has(this.cellKey(rowIdx, colIdx));
  }

  protected isCellNull(rowIdx: number, colIdx: number): boolean {
    return this.cellValue(rowIdx, colIdx) === NULL_TOKEN;
  }

  /** User typed into a cell. If the new value matches the server value, drop the pending edit
   *  (so the cell stops being dirty). Otherwise record the pending value. */
  protected onCellInput(rowIdx: number, colIdx: number, value: string): void {
    const key = this.cellKey(rowIdx, colIdx);
    const server = this.serverCell(rowIdx, colIdx);
    this.pendingEdits.update((m) => {
      const next = new Map(m);
      // value === '' is NOT the same as server === null - empty string is a real value.
      if (server !== null && value === server) next.delete(key);
      else next.set(key, value);
      return next;
    });
  }

  /** Backspace on an already-empty cell rewrites it to NULL. Typing any other key while NULL
   *  cancels the NULL and starts a fresh string (handled by the input's normal input event). */
  protected onCellKeydown(rowIdx: number, colIdx: number, ev: KeyboardEvent): void {
    if (ev.key !== 'Backspace') return;
    const v = this.cellValue(rowIdx, colIdx);
    if (v === '' || v === NULL_TOKEN) {
      ev.preventDefault();
      const key = this.cellKey(rowIdx, colIdx);
      this.pendingEdits.update((m) => new Map(m).set(key, NULL_TOKEN));
    }
  }

  protected discardChanges(): void {
    this.pendingEdits.set(new Map());
    this.editError.set(null);
  }

  /** PATCH every dirty row in a single bulk request. The backend wraps the batch in one
   *  transaction (on Postgres-family engines) so Save is all-or-nothing - on any per-row
   *  failure the whole thing rolls back and no rows in the table change. */
  protected saveChanges(): void {
    const r = this.rows();
    const id = this.instanceId(); const db = this.database(); const tbl = this.table();
    if (!r || !id || !db || !tbl) return;
    const edits = this.pendingEdits();
    if (edits.size === 0) return;

    // Group keys by rowIdx.
    const rowsToSave = new Set<number>();
    for (const key of edits.keys()) rowsToSave.add(Number(key.split(':')[0]));

    this.saving.set(true);
    this.editError.set(null);

    const sequence = Array.from(rowsToSave).sort((a, b) => a - b);
    // Materialise the new value vector for each dirty row, then send everything in one call.
    const updates = sequence.map((rowIdx) => {
      const original = r.rows[rowIdx];
      const newValues = original.map((_, colIdx) => {
        const v = this.cellValue(rowIdx, colIdx);
        return v === NULL_TOKEN ? null : v;
      });
      return { rowIdx, originalValues: original, newValues };
    });

    this.api.apiDatabaseIdBrowseDatabaseTablesTableRowsBulkPatch(id, db, tbl, {
      columns: r.columns,
      updates: updates.map(({ originalValues, newValues }) => ({ originalValues, newValues })),
    } as any).subscribe({
      next: () => {
        this.rows.update((curr) => {
          if (curr === null) return curr;
          const byRow = new Map(updates.map((u) => [u.rowIdx, u.newValues]));
          return {
            ...curr,
            rows: curr.rows.map((row, j) => byRow.has(j) ? (byRow.get(j) as unknown as string[]) : row),
          };
        });
        this.pendingEdits.set(new Map());
        this.saving.set(false);
      },
      error: (err) => { this.editError.set(messageOf(err)); this.saving.set(false); },
    });
  }

  protected saveDraft(): void {
    const d = this.draft(); const r = this.rows();
    const id = this.instanceId(); const db = this.database(); const tbl = this.table();
    if (!d || !r || !id || !db || !tbl) return;

    const newValues = d.values.map((v) => (v === NULL_TOKEN ? null : v));
    this.saving.set(true);
    this.editError.set(null);

    if (d.mode === 'insert') {
      // Strip DB-owned columns (id) so the database generates them. Otherwise an empty
      // string from the input would either be rejected (SERIAL NOT NULL) or coerced.
      const insertCols: string[] = [];
      const insertVals: (string | null)[] = [];
      for (let i = 0; i < r.columns.length; i++) {
        if (r.columns[i].toLowerCase() === 'id') continue;
        insertCols.push(r.columns[i]);
        insertVals.push(newValues[i]);
      }
      this.api.apiDatabaseIdBrowseDatabaseTablesTableRowsPost(id, db, tbl, {
        columns: insertCols,
        values: insertVals,
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

import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlay, lucideTerminal, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { DatabaseService } from '../api/api/database.service';
import { DatabaseInstanceDto } from '../api/model/databaseInstanceDto';
import { RunQueryResultDto } from '../api/model/runQueryResultDto';

// Engines whose IInnerQueryService is registered on the backend. Surface other engines as
// "console not available" without making the API roundtrip.
const SUPPORTED_ENGINES = new Set<string>([
  'postgres', 'timescaledb', 'pgvector',
  'cockroachdb',
  'mysql', 'mariadb',
  'mssql',
  'clickhouse',
]);

@Component({
  selector: 'app-query',
  imports: [
    ContentHeader,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmInputImports,
    HlmSelectImports,
    HlmTableImports,
  ],
  providers: [provideIcons({ lucidePlay, lucideTerminal, lucideTriangleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

    <section class="flex flex-col gap-4 p-6">
      <div class="flex items-center gap-2">
        <ng-icon name="lucideTerminal" size="20" />
        <h1 class="text-xl font-semibold">Query console</h1>
      </div>
      <p class="text-muted-foreground text-sm">
        Run SQL against any provisioned instance. Read or write, your choice.
      </p>

      <div class="flex flex-wrap items-end gap-3">
        <div class="flex min-w-[16rem] flex-1 flex-col gap-1.5">
          <label hlmLabel class="text-muted-foreground text-xs uppercase tracking-wide">Instance</label>
          <hlm-select [value]="instanceId()" (valueChange)="instanceId.set($event)">
            <hlm-select-trigger class="w-full">
              <hlm-select-value placeholder="Select an instance" />
            </hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              @for (i of instances(); track i.id) {
                <hlm-select-item [value]="i.id">
                  {{ i.engine }} - {{ i.containerName }}
                </hlm-select-item>
              }
            </hlm-select-content>
          </hlm-select>
        </div>

        <div class="flex min-w-[12rem] flex-1 flex-col gap-1.5">
          <label hlmLabel class="text-muted-foreground text-xs uppercase tracking-wide">Database</label>
          <hlm-select [value]="database()" (valueChange)="database.set($event)" [disabled]="databases().length === 0">
            <hlm-select-trigger class="w-full">
              <hlm-select-value placeholder="Select a database" />
            </hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal>
              @for (d of databases(); track d) {
                <hlm-select-item [value]="d">{{ d }}</hlm-select-item>
              }
            </hlm-select-content>
          </hlm-select>
        </div>
      </div>

      @if (selectedInstance(); as inst) {
        @if (!isSupported()) {
          <div class="border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-300 flex items-center gap-2 rounded-md border p-3 text-sm">
            <ng-icon name="lucideTriangleAlert" size="16" />
            The query console is not available for <code class="font-mono">{{ inst.engine }}</code> yet. Supported engines: postgres, timescaledb, pgvector, cockroachdb, mysql, mariadb, mssql, clickhouse.
          </div>
        } @else {
          <div class="flex flex-col gap-2">
            <textarea
              hlmInput
              class="font-mono min-h-[10rem] resize-y"
              placeholder="SELECT 1;"
              [value]="sql()"
              (input)="sql.set(toValue($event))"
              (keydown)="onKey($event)"
              aria-label="SQL editor"
            ></textarea>
            <div class="flex items-center justify-between gap-2">
              <span class="text-muted-foreground text-xs">
                Ctrl/Cmd + Enter to run. Results are capped at {{ rowLimit() }} rows.
              </span>
              <button hlmBtn type="button" (click)="run()" [disabled]="!canRun()">
                <ng-icon name="lucidePlay" size="14" />
                {{ running() ? 'Running...' : 'Run query' }}
              </button>
            </div>
          </div>

          @if (error(); as err) {
            <pre class="border-destructive/40 bg-destructive/10 text-destructive whitespace-pre-wrap rounded-md border p-3 text-sm">{{ err }}</pre>
          }

          @if (result(); as r) {
            <div class="flex items-center gap-2 text-xs">
              <span hlmBadge>{{ r.rowsAffected }} {{ r.columns.length > 0 ? 'rows' : 'rows affected' }}</span>
              <span hlmBadge>{{ r.elapsedMs }} ms</span>
              @if (r.truncated) {
                <span hlmBadge variant="destructive">truncated</span>
              }
            </div>

            @if (r.columns.length > 0) {
              <div hlmTable class="text-xs">
                <div hlmTHead>
                  <div hlmTr>
                    @for (c of r.columns; track c.name) {
                      <div hlmTh class="font-mono">
                        {{ c.name }}
                        <span class="text-muted-foreground">({{ c.typeName }})</span>
                      </div>
                    }
                  </div>
                </div>
                <div hlmTBody>
                  @for (row of r.rows; track $index) {
                    <div hlmTr>
                      @for (cell of row; track $index) {
                        <div hlmTd class="font-mono">
                          @if (cell === null) {
                            <span class="text-muted-foreground italic">NULL</span>
                          } @else {
                            {{ cell }}
                          }
                        </div>
                      }
                    </div>
                  }
                </div>
              </div>
            }
          }
        }
      } @else {
        <p class="text-muted-foreground rounded-md border border-dashed p-6 text-center text-sm">
          Pick an instance to start writing queries.
        </p>
      }
    </section>
  `,
})
export class Query {
  private readonly api = inject(DatabaseService);

  protected readonly instances = signal<ReadonlyArray<DatabaseInstanceDto>>([]);
  protected readonly instanceId = signal<string | null>(null);
  protected readonly databases = signal<ReadonlyArray<string>>([]);
  protected readonly database = signal<string | null>(null);
  protected readonly sql = signal('');
  protected readonly running = signal(false);
  protected readonly result = signal<RunQueryResultDto | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly rowLimit = signal(250);

  protected readonly selectedInstance = computed(() => {
    const id = this.instanceId();
    return this.instances().find((i) => i.id === id) ?? null;
  });

  protected readonly isSupported = computed(() => {
    const inst = this.selectedInstance();
    return inst ? SUPPORTED_ENGINES.has(inst.engine) : false;
  });

  protected readonly canRun = computed(() =>
    !this.running() && this.isSupported() && this.database() !== null && this.sql().trim().length > 0,
  );

  constructor() {
    this.api.apiDatabaseGet().subscribe({
      next: (list) => this.instances.set(list),
      error: (err) => this.error.set(messageOf(err)),
    });

    // Refetch the per-instance database list whenever the picker changes.
    effect(() => {
      const id = this.instanceId();
      this.databases.set([]);
      this.database.set(null);
      this.result.set(null);
      this.error.set(null);
      if (!id) return;
      this.api.apiDatabaseIdDatabasesGet(id).subscribe({
        next: (dbs) => {
          this.databases.set(dbs);
          // Prefer the instance's provisioned default DB; fall back to the only DB present.
          const instance = this.instances().find((i) => i.id === id);
          const preferred = instance && dbs.includes(instance.databaseName) ? instance.databaseName : null;
          this.database.set(preferred ?? (dbs.length === 1 ? dbs[0] : null));
        },
        error: (err) => this.error.set(messageOf(err)),
      });
    });
  }

  protected toValue(event: Event): string {
    return (event.target as HTMLTextAreaElement).value;
  }

  protected onKey(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
      event.preventDefault();
      this.run();
    }
  }

  protected run(): void {
    if (!this.canRun()) return;
    const id = this.instanceId();
    const db = this.database();
    if (!id || !db) return;
    this.running.set(true);
    this.error.set(null);
    this.result.set(null);
    this.api
      .apiDatabaseIdQueryPost(id, { database: db, sql: this.sql(), rowLimit: this.rowLimit() })
      .subscribe({
        next: (r) => {
          this.result.set(r);
          this.running.set(false);
        },
        error: (err) => {
          this.error.set(messageOf(err));
          this.running.set(false);
        },
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

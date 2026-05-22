import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlay, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { DatabaseService } from '../../../api/api/database.service';
import { RunQueryResultDto } from '../../../api/model/runQueryResultDto';

// Engines whose IInnerQueryService is registered on the backend. Surface other engines as
// "console not available" without making the API roundtrip.
export const QUERY_SUPPORTED_ENGINES = new Set<string>([
  'postgres', 'timescaledb', 'pgvector',
  'cockroachdb',
  'mysql', 'mariadb',
  'mssql',
  'clickhouse',
]);

@Component({
  selector: 'app-query-console',
  imports: [NgIcon, HlmBadgeImports, HlmButtonImports, HlmInputImports, HlmTableImports],
  providers: [provideIcons({ lucidePlay, lucideTriangleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (instanceId() === null || database() === null) {
      <p class="text-muted-foreground flex flex-1 items-center justify-center text-sm">
        Pick an instance and database to start writing queries.
      </p>
    } @else if (!isSupported()) {
      <div class="border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-300 m-4 flex items-center gap-2 rounded-md border p-3 text-sm">
        <ng-icon name="lucideTriangleAlert" size="16" />
        The query console is not available for <code class="font-mono">{{ engine() }}</code> yet. Supported engines: postgres, timescaledb, pgvector, cockroachdb, mysql, mariadb, mssql, clickhouse.
      </div>
    } @else {
      <div class="flex flex-col gap-2 p-4">
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
            Ctrl/Cmd + Enter to run. Results capped at {{ rowLimit() }} rows.
          </span>
          <button hlmBtn type="button" (click)="run()" [disabled]="!canRun()">
            <ng-icon name="lucidePlay" size="14" />
            {{ running() ? 'Running...' : 'Run query' }}
          </button>
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
      </div>
    }
  `,
})
export class QueryConsole {
  readonly instanceId = input<string | null>(null);
  readonly database = input<string | null>(null);
  readonly engine = input<string | null>(null);

  private readonly api = inject(DatabaseService);

  protected readonly sql = signal('');
  protected readonly running = signal(false);
  protected readonly result = signal<RunQueryResultDto | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly rowLimit = signal(250);

  protected readonly isSupported = computed(() => {
    const e = this.engine();
    return e ? QUERY_SUPPORTED_ENGINES.has(e) : false;
  });

  protected readonly canRun = computed(() =>
    !this.running() &&
    this.isSupported() &&
    this.instanceId() !== null &&
    this.database() !== null &&
    this.sql().trim().length > 0,
  );

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

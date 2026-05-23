import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  OnDestroy,
  signal,
  viewChild,
} from '@angular/core';
import { sql } from '@codemirror/lang-sql';
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language';
import { EditorState } from '@codemirror/state';
import { EditorView, keymap } from '@codemirror/view';
import { tags as t } from '@lezer/highlight';
import { basicSetup } from 'codemirror';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlay, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { DatabaseService } from '../../../api/api/database.service';
import { RunQueryResultDto } from '../../../api/model/runQueryResultDto';
import { TableSummaryDto } from '../../../api/model/tableSummaryDto';

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
  imports: [NgIcon, HlmBadgeImports, HlmButtonImports, HlmTableImports],
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
        <!-- CodeMirror host. The editor is created in afterViewInit and re-uses this div for
             its full lifetime; signal -> editor sync only flows on programmatic value changes. -->
        <div #editorHost class="border-input rounded-md border overflow-hidden text-sm"></div>
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
            <div class="border-input overflow-x-auto rounded-md border">
              <table hlmTable class="text-xs">
                <thead hlmTableHeader>
                  <tr hlmTableRow>
                    @for (c of r.columns; track c.name) {
                      <th hlmTableHead class="font-mono whitespace-nowrap">
                        {{ c.name }}
                        <span class="text-muted-foreground">({{ c.typeName }})</span>
                      </th>
                    }
                  </tr>
                </thead>
                <tbody hlmTableBody>
                  @for (row of r.rows; track $index) {
                    <tr hlmTableRow>
                      @for (cell of row; track $index) {
                        <td hlmTableCell class="font-mono whitespace-nowrap align-top">
                          @if (cell === null) {
                            <span class="text-muted-foreground italic">NULL</span>
                          } @else {
                            {{ cell }}
                          }
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      </div>
    }
  `,
})
export class QueryConsole implements AfterViewInit, OnDestroy {
  readonly instanceId = input<string | null>(null);
  readonly database = input<string | null>(null);
  readonly engine = input<string | null>(null);
  /** Optional: table list to enrich the SQL autocomplete with the current database's tables. */
  readonly tables = input<ReadonlyArray<TableSummaryDto>>([]);

  private readonly api = inject(DatabaseService);
  private readonly host = viewChild<ElementRef<HTMLDivElement>>('editorHost');

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

  private view: EditorView | null = null;

  constructor() {
    // Rebuild the editor if the engine + database changes so the autocomplete schema reflects
    // the new context. We tear down + remount instead of dynamically swapping extensions to
    // keep the wiring simple - the editor host is cheap to recreate.
    effect(() => {
      this.tables();
      this.engine();
      if (this.view) this.rebuild();
    });
  }

  ngAfterViewInit(): void {
    this.rebuild();
  }

  ngOnDestroy(): void {
    this.view?.destroy();
    this.view = null;
  }

  private rebuild(): void {
    const host = this.host()?.nativeElement;
    if (!host) return;
    this.view?.destroy();

    const schema: Record<string, string[]> = {};
    for (const t of this.tables()) schema[t.name] = [];

    // We don't know the columns ahead of fetching them, so just feed the table names.
    // CodeMirror's SQL completion will mix them in with keywords + the engine dialect.
    const dialect = this.engine() ?? 'postgres';
    const isDark = document.documentElement.classList.contains('dark');

    const startDoc = this.sql();
    const state = EditorState.create({
      doc: startDoc,
      extensions: [
        // basicSetup is the canonical bundle from `codemirror`: line numbers, history,
        // default + history keymaps, indentation, search, bracket matching, code folding,
        // autocompletion, syntax highlighting. We add SQL on top + our own Mod-Enter hook.
        // Putting our keymap before basicSetup ensures Ctrl/Cmd+Enter runs the query instead
        // of inserting a newline (basicSetup's default behaviour).
        keymap.of([
          { key: 'Mod-Enter', preventDefault: true, run: () => { this.run(); return true; } },
        ]),
        basicSetup,
        sql({ schema, upperCaseKeywords: true }),
        EditorView.updateListener.of((u) => {
          if (u.docChanged) this.sql.set(u.state.doc.toString());
        }),
        krintEditorTheme(isDark),
        syntaxHighlighting(krintHighlightStyle(isDark)),
      ],
    });

    this.view = new EditorView({ state, parent: host });
    // Avoid an unused-variable warning in modes where we don't reference dialect explicitly.
    void dialect;
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

// Editor chrome theme. Pulls colors from the Spartan/Tailwind tokens already on the document
// so the editor visually blends with the rest of the app in both light and dark mode. We
// avoid raw hex values; everything is hsl(var(--token)) or a transparency on currentColor.
function krintEditorTheme(_isDark: boolean): import('@codemirror/state').Extension {
  return EditorView.theme({
    '&': {
      height: '14rem',
      fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
      backgroundColor: 'transparent',
      color: 'hsl(var(--foreground))',
      fontSize: '13px',
    },
    '.cm-scroller': { overflow: 'auto', fontFamily: 'inherit' },
    '.cm-content': { caretColor: 'hsl(var(--foreground))' },
    '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'hsl(var(--foreground))' },
    '&.cm-focused .cm-selectionBackground, ::selection': {
      backgroundColor: 'hsl(var(--primary) / 0.25)',
    },
    '.cm-selectionBackground': { backgroundColor: 'hsl(var(--primary) / 0.15)' },
    '.cm-gutters': {
      backgroundColor: 'transparent',
      color: 'hsl(var(--muted-foreground))',
      borderRight: '1px solid hsl(var(--border))',
    },
    '.cm-activeLineGutter, .cm-activeLine': {
      backgroundColor: 'hsl(var(--muted) / 0.4)',
    },
    '.cm-foldGutter .cm-gutterElement': { color: 'hsl(var(--muted-foreground))' },
    // Tone the autocomplete popup to match Spartan's card surface.
    '.cm-tooltip.cm-tooltip-autocomplete': {
      backgroundColor: 'hsl(var(--popover))',
      color: 'hsl(var(--popover-foreground))',
      border: '1px solid hsl(var(--border))',
      borderRadius: '6px',
      boxShadow: '0 4px 12px rgba(0,0,0,0.25)',
    },
    '.cm-tooltip.cm-tooltip-autocomplete > ul > li[aria-selected]': {
      backgroundColor: 'hsl(var(--accent))',
      color: 'hsl(var(--accent-foreground))',
    },
    '.cm-tooltip.cm-tooltip-autocomplete > ul > li': { padding: '2px 8px' },
    // Search panel + the matching-bracket halo.
    '.cm-panels': { backgroundColor: 'hsl(var(--card))', color: 'hsl(var(--card-foreground))' },
    '.cm-matchingBracket, .cm-nonmatchingBracket': {
      backgroundColor: 'hsl(var(--primary) / 0.15)',
      outline: '1px solid hsl(var(--primary) / 0.4)',
    },
  });
}

// Syntax palette tuned for both themes. We pick colors that exist in Tailwind's slate/indigo/
// emerald families so they stay readable on the Spartan dark canvas without the saturated
// punch of oneDark. In light mode we shift to deeper variants of the same hues.
function krintHighlightStyle(isDark: boolean): HighlightStyle {
  const colors = isDark
    ? {
        keyword: '#c084fc',   // violet-400
        type:    '#7dd3fc',   // sky-300
        builtin: '#7dd3fc',
        number:  '#f0abfc',   // fuchsia-300
        string:  '#86efac',   // emerald-300
        comment: '#94a3b8',   // slate-400
        meta:    '#fda4af',   // rose-300
        ident:   'hsl(var(--foreground))',
        punct:   '#94a3b8',
      }
    : {
        keyword: '#7c3aed',   // violet-600
        type:    '#0369a1',   // sky-700
        builtin: '#0369a1',
        number:  '#a21caf',   // fuchsia-700
        string:  '#047857',   // emerald-700
        comment: '#64748b',   // slate-500
        meta:    '#be123c',   // rose-700
        ident:   'hsl(var(--foreground))',
        punct:   '#64748b',
      };

  return HighlightStyle.define([
    { tag: t.keyword, color: colors.keyword, fontWeight: '600' },
    { tag: [t.controlKeyword, t.operatorKeyword, t.modifier], color: colors.keyword },
    { tag: [t.typeName, t.className], color: colors.type },
    { tag: [t.standard(t.variableName), t.special(t.variableName)], color: colors.builtin },
    { tag: t.number, color: colors.number },
    { tag: [t.string, t.special(t.string)], color: colors.string },
    { tag: t.comment, color: colors.comment, fontStyle: 'italic' },
    { tag: t.meta, color: colors.meta },
    { tag: t.variableName, color: colors.ident },
    { tag: [t.punctuation, t.bracket], color: colors.punct },
    { tag: t.operator, color: colors.punct },
    { tag: t.null, color: colors.meta },
    { tag: t.bool, color: colors.meta },
  ]);
}

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  if (err instanceof Error) return err.message;
  return 'Request failed';
}

import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnDestroy, computed, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideEraser,
  lucidePause,
  lucidePlay,
  lucideRefreshCw,
  lucideTerminal,
} from '@ng-icons/lucide';
import {
  simpleApachecassandra,
  simpleApachecouchdb,
  simpleClickhouse,
  simpleCockroachlabs,
  simpleElasticsearch,
  simpleMariadb,
  simpleMongodb,
  simpleMysql,
  simpleNeo4j,
  simplePostgresql,
  simpleRedis,
  simpleTimescale,
} from '@ng-icons/simple-icons';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { HubConnection } from '@microsoft/signalr';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { WebLinksAddon } from '@xterm/addon-web-links';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { customMssql, customQdrant, customValkey } from '../shared/icons/custom-icons';
import { DatabaseService } from '../api/api/database.service';
import { DatabaseInstanceDto } from '../api/model/databaseInstanceDto';
import { ContainerHubService } from './container-hub.service';

type ActiveTab = 'logs' | 'exec';

@Component({
  selector: 'app-console',
  imports: [
    ContentHeader,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmTooltipImports,
  ],
  providers: [
    provideIcons({
      lucideEraser,
      lucidePause,
      lucidePlay,
      lucideRefreshCw,
      lucideTerminal,
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
  host: { class: 'flex flex-1 min-h-0 flex-col' },
  templateUrl: './console.html',
})
export class Console implements OnDestroy {
  private readonly api = inject(DatabaseService);
  private readonly hub = inject(ContainerHubService);

  protected readonly instances = signal<ReadonlyArray<DatabaseInstanceDto>>([]);
  protected readonly instanceId = signal<string | null>(null);
  protected readonly activeTab = signal<ActiveTab>('logs');
  protected readonly logsPaused = signal(false);
  protected readonly logsStatus = signal<'idle' | 'connecting' | 'streaming' | 'error'>('idle');
  protected readonly execStatus = signal<'idle' | 'connecting' | 'open' | 'closed'>('idle');

  protected readonly selectedInstance = computed(() => {
    const id = this.instanceId();
    return id ? this.instances().find((i) => i.id === id) ?? null : null;
  });

  private readonly logsContainer = viewChild<ElementRef<HTMLDivElement>>('logsContainer');
  private readonly execContainer = viewChild<ElementRef<HTMLDivElement>>('execContainer');

  private logsTerm: Terminal | null = null;
  private logsFit: FitAddon | null = null;
  private logsSub: { dispose(): void } | null = null;
  private logsBuffer: string[] = [];

  private execTerm: Terminal | null = null;
  private execFit: FitAddon | null = null;
  private execSessionId: string | null = null;
  private execOutputHandler: ((sessionId: string, base64: string) => void) | null = null;
  private execExitedHandler: ((sessionId: string, code: number | null) => void) | null = null;
  private execInputDisposable: { dispose(): void } | null = null;
  private execResizeDisposable: { dispose(): void } | null = null;

  private hubConnection: HubConnection | null = null;
  private resizeObserver: ResizeObserver | null = null;

  constructor() {
    this.api.apiDatabaseGet().pipe(takeUntilDestroyed()).subscribe({
      next: (list) => {
        this.instances.set(list);
        if (!this.instanceId() && list.length > 0) this.instanceId.set(list[0].id!);
      },
    });

    // (Re)mount xterm into its host whenever instance or tab changes, then start the relevant
    // SignalR stream. Always tear down the previous stream first so switching instances doesn't
    // leak hub subscriptions.
    effect(() => {
      const id = this.instanceId();
      const tab = this.activeTab();
      this.teardownLogs();
      this.teardownExec();
      if (!id) return;
      // The DOM container is wrapped in @if blocks; defer one macrotask so viewChild updates.
      setTimeout(() => {
        if (this.instanceId() !== id || this.activeTab() !== tab) return;
        if (tab === 'logs') this.startLogs(id);
        else this.startExec(id);
      }, 0);
    });
  }

  ngOnDestroy(): void {
    this.teardownLogs();
    this.teardownExec();
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
    // Close the SignalR connection when leaving /console — nothing else uses the hub, so
    // there's no point keeping the WebSocket alive while the user is on another page.
    void this.hub.stop();
  }

  protected selectInstance(id: string | null): void {
    this.instanceId.set(id);
  }

  protected setTab(tab: ActiveTab): void {
    this.activeTab.set(tab);
  }

  protected togglePause(): void {
    this.logsPaused.update((p) => !p);
    if (!this.logsPaused() && this.logsBuffer.length > 0 && this.logsTerm) {
      const drained = this.logsBuffer.splice(0, this.logsBuffer.length);
      for (const c of drained) this.logsTerm.write(c);
    }
  }

  protected clearLogs(): void {
    this.logsTerm?.clear();
    this.logsBuffer.length = 0;
  }

  protected refresh(): void {
    const id = this.instanceId();
    const tab = this.activeTab();
    this.teardownLogs();
    this.teardownExec();
    if (!id) return;
    setTimeout(() => {
      if (tab === 'logs') this.startLogs(id);
      else this.startExec(id);
    }, 0);
  }

  protected engineIcon(engine: string): string {
    switch (engine) {
      case 'postgres': return 'simplePostgresql';
      case 'mysql': return 'simpleMysql';
      case 'mongo': return 'simpleMongodb';
      case 'mariadb': return 'simpleMariadb';
      case 'timescaledb': return 'simpleTimescale';
      case 'redis': return 'simpleRedis';
      case 'cockroachdb': return 'simpleCockroachlabs';
      case 'clickhouse': return 'simpleClickhouse';
      case 'cassandra': return 'simpleApachecassandra';
      case 'couchdb': return 'simpleApachecouchdb';
      case 'elasticsearch': return 'simpleElasticsearch';
      case 'pgvector': return 'simplePostgresql';
      case 'neo4j': return 'simpleNeo4j';
      case 'qdrant': return 'customQdrant';
      case 'valkey': return 'customValkey';
      case 'mssql': return 'customMssql';
      default: return 'simplePostgresql';
    }
  }

  protected instanceUrl(i: DatabaseInstanceDto): string {
    return `${i.host}:${i.port}/${i.databaseName}`;
  }

  private buildTerminal(host: HTMLElement): { term: Terminal; fit: FitAddon } {
    const term = new Terminal({
      cursorBlink: true,
      fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", monospace',
      fontSize: 13,
      scrollback: 5000,
      convertEol: true,
      theme: {
        background: '#0a0a0a',
        foreground: '#e5e5e5',
        cursor: '#e5e5e5',
        selectionBackground: '#3a3a3a',
      },
    });
    const fit = new FitAddon();
    term.loadAddon(fit);
    term.loadAddon(new WebLinksAddon());
    term.open(host);
    queueMicrotask(() => {
      try { fit.fit(); } catch { /* host not laid out yet */ }
    });
    this.resizeObserver?.disconnect();
    this.resizeObserver = new ResizeObserver(() => {
      try { fit.fit(); } catch { /* ignore */ }
      if (this.activeTab() === 'exec' && this.execSessionId && this.hubConnection) {
        const cols = term.cols;
        const rows = term.rows;
        this.hubConnection.invoke('ResizeExec', this.execSessionId, cols, rows).catch(() => { });
      }
    });
    this.resizeObserver.observe(host);
    return { term, fit };
  }

  private async startLogs(instanceId: string): Promise<void> {
    this.logsStatus.set('connecting');
    const host = this.logsContainer()?.nativeElement;
    if (!host) return;
    const { term, fit } = this.buildTerminal(host);
    this.logsTerm = term;
    this.logsFit = fit;

    try {
      const conn = await this.hub.getConnection();
      this.hubConnection = conn;
      const stream = conn.stream<string>('StreamLogs', instanceId);
      this.logsStatus.set('streaming');
      this.logsSub = stream.subscribe({
        next: (chunk) => {
          if (this.logsPaused()) {
            this.logsBuffer.push(chunk);
            if (this.logsBuffer.length > 1000) this.logsBuffer.shift();
            return;
          }
          term.write(chunk);
        },
        error: () => this.logsStatus.set('error'),
        complete: () => this.logsStatus.set('idle'),
      });
    } catch {
      this.logsStatus.set('error');
      term.writeln('\x1b[31m[failed to connect to container logs hub]\x1b[0m');
    }
  }

  private teardownLogs(): void {
    try { this.logsSub?.dispose(); } catch { }
    this.logsSub = null;
    try { this.logsTerm?.dispose(); } catch { }
    this.logsTerm = null;
    this.logsFit = null;
    this.logsBuffer.length = 0;
    this.logsStatus.set('idle');
  }

  private async startExec(instanceId: string): Promise<void> {
    this.execStatus.set('connecting');
    const host = this.execContainer()?.nativeElement;
    if (!host) return;
    const { term, fit } = this.buildTerminal(host);
    this.execTerm = term;
    this.execFit = fit;

    try {
      const conn = await this.hub.getConnection();
      this.hubConnection = conn;

      this.execOutputHandler = (sessionId: string, base64: string) => {
        if (sessionId !== this.execSessionId) return;
        const bytes = base64ToBytes(base64);
        // xterm.js write accepts Uint8Array since 5.x; if string-only, decode here.
        term.write(bytes);
      };
      this.execExitedHandler = (sessionId: string, code: number | null) => {
        if (sessionId !== this.execSessionId) return;
        term.writeln(`\r\n\x1b[33m[exec exited: code ${code ?? '?'}]\x1b[0m`);
        this.execStatus.set('closed');
      };
      conn.on('ExecOutput', this.execOutputHandler);
      conn.on('ExecExited', this.execExitedHandler);

      const sessionId = await conn.invoke<string>('StartExec', instanceId, term.cols, term.rows);
      this.execSessionId = sessionId;
      this.execStatus.set('open');

      this.execInputDisposable = term.onData((data) => {
        if (!this.execSessionId) return;
        const base64 = bytesToBase64(new TextEncoder().encode(data));
        conn.invoke('WriteExec', this.execSessionId, base64).catch((err) => {
          term.writeln(`\r\n\x1b[31m[write failed: ${err?.message ?? err}]\x1b[0m`);
        });
      });
      this.execResizeDisposable = term.onResize(({ cols, rows }) => {
        if (!this.execSessionId) return;
        conn.invoke('ResizeExec', this.execSessionId, cols, rows).catch(() => { });
      });
      term.focus();
    } catch {
      this.execStatus.set('closed');
      term.writeln('\x1b[31m[failed to open exec session]\x1b[0m');
    }
  }

  private teardownExec(): void {
    try { this.execInputDisposable?.dispose(); } catch { }
    try { this.execResizeDisposable?.dispose(); } catch { }
    this.execInputDisposable = null;
    this.execResizeDisposable = null;
    if (this.hubConnection) {
      if (this.execOutputHandler) this.hubConnection.off('ExecOutput', this.execOutputHandler);
      if (this.execExitedHandler) this.hubConnection.off('ExecExited', this.execExitedHandler);
    }
    this.execOutputHandler = null;
    this.execExitedHandler = null;
    if (this.execSessionId && this.hubConnection) {
      const id = this.execSessionId;
      this.hubConnection.invoke('EndExec', id).catch(() => { });
    }
    this.execSessionId = null;
    try { this.execTerm?.dispose(); } catch { }
    this.execTerm = null;
    this.execFit = null;
    this.execStatus.set('idle');
  }
}

function bytesToBase64(bytes: Uint8Array): string {
  let s = '';
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
  return btoa(s);
}

function base64ToBytes(base64: string): Uint8Array {
  const s = atob(base64);
  const bytes = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) bytes[i] = s.charCodeAt(i);
  return bytes;
}

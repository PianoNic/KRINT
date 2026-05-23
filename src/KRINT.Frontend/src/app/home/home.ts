import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheckBig, lucideCpu, lucideDatabase, lucideMemoryStick, lucidePlus, lucideRefreshCw } from '@ng-icons/lucide';
import { simpleApachecassandra, simpleApachecouchdb, simpleClickhouse, simpleCockroachlabs, simpleElasticsearch, simpleMariadb, simpleMongodb, simpleMysql, simpleNeo4j, simplePostgresql, simpleRedis, simpleTimescale } from '@ng-icons/simple-icons';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { customMssql, customQdrant, customValkey } from '../shared/icons/custom-icons';
import { DashboardService } from '../api/api/dashboard.service';
import { DashboardStatsDto } from '../api/model/dashboardStatsDto';

@Component({
  selector: 'app-home',
  imports: [
    ContentHeader,
    DatePipe,
    RouterLink,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
  ],
  providers: [
    provideIcons({
      lucideCircleCheckBig,
      lucideCpu,
      lucideDatabase,
      lucideMemoryStick,
      lucidePlus,
      lucideRefreshCw,
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
  template: `
    <app-content-header />

    <section class="flex flex-1 min-h-0 flex-col gap-4 overflow-auto border-t p-4">
      <header class="flex items-center justify-between gap-2">
        <div class="min-w-0">
          <h1 class="text-2xl font-semibold">Welcome to KRINT</h1>
          <p class="text-muted-foreground text-sm">One click. One key. Your database is ready.</p>
        </div>
        <div class="flex items-center gap-2">
          <a hlmBtn size="sm" routerLink="/create">
            <ng-icon name="lucidePlus" size="16" />
            Create
          </a>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="reload()" [disabled]="loading()">
            <ng-icon name="lucideRefreshCw" size="14" />
            {{ loading() ? 'Loading…' : 'Refresh' }}
          </button>
        </div>
      </header>

      @if (error(); as err) {
        <p class="text-destructive text-sm">{{ err }}</p>
      }

      <!-- KPI row. Each card stays a fixed shape so the grid is predictable on small widths. -->
      <div class="grid grid-cols-2 gap-4 md:grid-cols-4">
        <article hlmCard class="p-4">
          <div class="text-muted-foreground flex items-center gap-2 text-xs uppercase tracking-wide">
            <ng-icon name="lucideDatabase" size="14" />
            Instances
          </div>
          <div class="mt-2 text-2xl font-semibold">{{ totalInstances() }}</div>
        </article>
        <article hlmCard class="p-4">
          <div class="text-muted-foreground flex items-center gap-2 text-xs uppercase tracking-wide">
            <ng-icon name="lucideCircleCheckBig" size="14" />
            Running
          </div>
          <div class="mt-2 flex items-baseline gap-2">
            <span class="text-2xl font-semibold">{{ runningInstances() }}</span>
            <span class="text-muted-foreground text-xs">of {{ totalInstances() }}</span>
          </div>
        </article>
        <article hlmCard class="p-4">
          <div class="text-muted-foreground flex items-center gap-2 text-xs uppercase tracking-wide">
            <ng-icon name="lucideMemoryStick" size="14" />
            Memory in use
          </div>
          <div class="mt-2 text-2xl font-semibold">{{ memoryLabel() }}</div>
        </article>
        <article hlmCard class="p-4">
          <div class="text-muted-foreground flex items-center gap-2 text-xs uppercase tracking-wide">
            <ng-icon name="lucideCpu" size="14" />
            Container CPU
          </div>
          <div class="mt-2 text-2xl font-semibold">{{ cpuLabel() }}</div>
        </article>
      </div>

      <!-- Per-engine breakdown + recent activity sit side-by-side on desktop, stack on mobile. -->
      <div class="grid flex-1 min-h-0 grid-cols-1 gap-4 lg:grid-cols-2">
        <section hlmCard class="flex min-h-0 flex-col">
          <div hlmCardHeader>
            <h2 hlmCardTitle>By engine</h2>
            <p hlmCardDescription>Distribution of provisioned instances.</p>
          </div>
          <div hlmCardContent class="min-h-0 flex-1 overflow-auto">
            @if ((stats()?.perEngine?.length ?? 0) === 0) {
              <p class="text-muted-foreground text-sm">No instances yet.</p>
            } @else {
              <ul class="divide-border divide-y">
                @for (e of stats()?.perEngine ?? []; track e.engine) {
                  <li class="flex items-center justify-between gap-3 py-2 text-sm">
                    <span class="inline-flex items-center gap-3">
                      <ng-icon [name]="engineIcon(e.engine)" size="18" />
                      <span class="font-medium">{{ e.engine }}</span>
                    </span>
                    <span hlmBadge variant="secondary" class="font-mono text-xs">{{ e.count }}</span>
                  </li>
                }
              </ul>
            }
          </div>
        </section>

        <section hlmCard class="flex min-h-0 flex-col">
          <div hlmCardHeader>
            <h2 hlmCardTitle>Recent activity</h2>
            <p hlmCardDescription>
              Latest events. <a routerLink="/activity" class="underline-offset-2 hover:underline">View all</a>
            </p>
          </div>
          <div hlmCardContent class="min-h-0 flex-1 overflow-auto">
            @if ((stats()?.recentActivity?.length ?? 0) === 0) {
              <p class="text-muted-foreground text-sm">Nothing here yet.</p>
            } @else {
              <ul class="divide-border divide-y">
                @for (a of stats()?.recentActivity ?? []; track a.id) {
                  <li class="flex items-center justify-between gap-3 py-2 text-sm">
                    <span class="inline-flex min-w-0 items-center gap-2">
                      <span hlmBadge variant="secondary" class="font-mono text-xs">{{ a.action }}</span>
                      <span class="text-muted-foreground truncate text-xs">{{ a.target }}</span>
                    </span>
                    <span class="text-muted-foreground shrink-0 font-mono text-xs">
                      {{ a.createdAt | date: 'HH:mm:ss' }}
                    </span>
                  </li>
                }
              </ul>
            }
          </div>
        </section>
      </div>
    </section>
  `,
})
export class Home {
  private readonly api = inject(DashboardService);

  protected readonly stats = signal<DashboardStatsDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  // The OpenAPI generator wraps int/long behind opaque interfaces (TotalInstances,
  // BackupEntryDtoSizeBytes) instead of `number`. We coerce here so the template can
  // do plain arithmetic without sprinkling `as any` everywhere.
  protected readonly totalInstances = computed(() => Number(this.stats()?.totalInstances ?? 0));
  protected readonly runningInstances = computed(() => Number(this.stats()?.runningInstances ?? 0));
  protected readonly memoryLabel = computed(() => formatBytes(Number(this.stats()?.totalMemoryBytes ?? 0)));
  protected readonly cpuLabel = computed(() => {
    const pct = Number(this.stats()?.totalCpuPercent ?? 0);
    return `${pct.toFixed(1)}%`;
  });

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.apiDashboardStatsGet().subscribe({
      next: (s) => { this.stats.set(s); this.loading.set(false); },
      error: (err) => {
        this.error.set(err instanceof Error ? err.message : 'Failed to load dashboard');
        this.loading.set(false);
      },
    });
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
      case 'neo4j':       return 'simpleNeo4j';
      case 'qdrant':      return 'customQdrant';
      case 'valkey':      return 'customValkey';
      case 'mssql':       return 'customMssql';
      default:         return 'lucideDatabase';
    }
  }
}

function formatBytes(bytes: number): string {
  if (!bytes || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const exponent = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  const value = bytes / Math.pow(1024, exponent);
  return `${value.toFixed(value >= 10 || exponent === 0 ? 0 : 1)} ${units[exponent]}`;
}

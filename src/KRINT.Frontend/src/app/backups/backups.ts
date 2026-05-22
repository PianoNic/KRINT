import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBrain,
  lucideCalendar,
  lucideCircleAlert,
  lucideDatabase,
  lucideDownload,
  lucideHistory,
  lucidePause,
  lucidePlay,
  lucidePlus,
  lucideRefreshCw,
  lucideTrash2,
} from '@ng-icons/lucide';
import { simpleApachecassandra, simpleApachecouchdb, simpleClickhouse, simpleCockroachlabs, simpleElasticsearch, simpleMariadb, simpleMongodb, simpleMysql, simpleNeo4j, simplePostgresql, simpleRedis, simpleTimescale } from '@ng-icons/simple-icons';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { environment } from '../shared/environments/environment';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { customMssql, customQdrant, customValkey } from '../shared/icons/custom-icons';
import { BackupsService } from '../api/api/backups.service';
import { DatabaseService } from '../api/api/database.service';
import { BackupEntryDto } from '../api/model/backupEntryDto';
import { BackupScheduleDto } from '../api/model/backupScheduleDto';
import { DatabaseInstanceDto } from '../api/model/databaseInstanceDto';
import { BackupScheduleDialogService } from './backup-schedule-dialog';

@Component({
  selector: 'app-backups',
  imports: [
    ContentHeader,
    DatePipe,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmSelectImports,
    HlmTableImports,
    HlmTooltipImports,
  ],
  providers: [
    provideIcons({
      lucideBrain,
      lucideCalendar,
      lucideCircleAlert,
      lucideDatabase,
      lucideDownload,
      lucideHistory,
      lucidePause,
      lucidePlay,
      lucidePlus,
      lucideRefreshCw,
      lucideTrash2,
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

    <section class="grid h-[calc(100vh-3rem)] grid-cols-[260px_1fr] gap-0 border-t">
      <!-- ===== Left rail: instance cards (also acts as the "Create a backup" picker) ===== -->
      <aside class="border-r flex flex-col overflow-hidden">
        <header class="flex items-center justify-between px-3 py-2 border-b">
          <div class="flex items-center gap-2">
            <span class="text-xs font-semibold uppercase tracking-wide">Instances</span>
            <span hlmBadge variant="secondary" class="px-1.5 py-0 text-[10px]">{{ instances().length }}</span>
          </div>
          <button hlmBtn variant="ghost" size="icon" type="button" class="h-7 w-7" aria-label="Refresh instances" hlmTooltip="Refresh" (click)="reloadInstances()">
            <ng-icon name="lucideRefreshCw" size="14" />
          </button>
        </header>
        <ul class="flex-1 overflow-y-auto">
          <li class="border-b">
            <button
              type="button"
              class="hover:bg-muted/60 flex w-full items-start gap-2 border-l-2 px-3 py-2 text-left transition-colors"
              [class.border-primary]="selectedInstanceId() === null"
              [class.bg-muted]="selectedInstanceId() === null"
              [class.border-transparent]="selectedInstanceId() !== null"
              (click)="selectedInstanceId.set(null)"
            >
              <ng-icon name="lucideDatabase" size="18" class="text-muted-foreground mt-0.5 shrink-0" />
              <div class="flex min-w-0 flex-1 flex-col">
                <span class="truncate text-sm font-medium">All instances</span>
                <span class="text-muted-foreground truncate text-[11px]">Schedules + backups across every instance</span>
              </div>
            </button>
          </li>
          @for (i of instances(); track i.id) {
            <li>
              <button
                type="button"
                class="hover:bg-muted/60 flex w-full items-start gap-2 border-l-2 px-3 py-2 text-left transition-colors"
                [class.border-primary]="i.id === selectedInstanceId()"
                [class.bg-muted]="i.id === selectedInstanceId()"
                [class.border-transparent]="i.id !== selectedInstanceId()"
                (click)="selectedInstanceId.set(i.id)"
              >
                <ng-icon [name]="engineIcon(i.engine)" size="18" class="mt-0.5 shrink-0" />
                <div class="flex min-w-0 flex-1 flex-col">
                  <span class="truncate text-sm font-medium">{{ i.displayName }}</span>
                  <span class="text-muted-foreground truncate font-mono text-[11px]">{{ instanceUrl(i) }}</span>
                </div>
                <span hlmBadge variant="secondary" class="shrink-0 px-1.5 py-0 text-[10px] uppercase">{{ i.engine }}</span>
              </button>
            </li>
          }
          @if (instances().length === 0) {
            <li class="text-muted-foreground p-4 text-center text-xs">No instances yet.</li>
          }
        </ul>
      </aside>

      <!-- ===== Right pane: contextual toolbar + schedules + backups ===== -->
      <main class="flex flex-col overflow-hidden">
        <header class="border-b flex items-center justify-between gap-2 px-4 py-2">
          <div class="flex min-w-0 flex-col">
            <span class="text-sm font-medium">
              {{ selectedInstance()?.displayName ?? 'All instances' }}
            </span>
            @if (selectedInstance(); as inst) {
              <span class="text-muted-foreground font-mono text-[11px]">{{ instanceUrl(inst) }}</span>
            } @else {
              <span class="text-muted-foreground text-[11px]">Pick an instance on the left to scope schedules + backups.</span>
            }
          </div>
          <div class="flex items-center gap-1">
            @if (selectedInstanceId()) {
              <button hlmBtn type="button" size="sm" [disabled]="creating()" (click)="create()">
                <ng-icon name="lucidePlus" size="14" />
                {{ creating() ? 'Snapshotting...' : 'Create backup' }}
              </button>
              <button hlmBtn variant="outline" size="sm" type="button" (click)="openScheduleDialog()">
                <ng-icon name="lucidePlus" size="14" />
                New schedule
              </button>
            }
            <button
              hlmBtn
              variant="outline"
              size="sm"
              type="button"
              (click)="reload()"
              [disabled]="loading()"
              aria-label="Refresh backups"
              hlmTooltip="Refresh"
            >
              <ng-icon name="lucideRefreshCw" size="14" />
            </button>
          </div>
        </header>

      <!-- Two equal-height cards. min-h-0 on each lets the inner table claim the available
           space without spilling the page; the card content itself becomes the scroll surface. -->
      <div class="grid flex-1 grid-rows-2 gap-4 overflow-hidden p-4">
      <section hlmCard class="flex min-h-0 flex-col">
        <div hlmCardHeader>
          <h2 hlmCardTitle class="flex items-center gap-2">
            <ng-icon name="lucideCalendar" size="18" />
            Schedules
          </h2>
        </div>

        <div hlmCardContent class="min-h-0 flex-1 overflow-auto">
          @if (filteredSchedules().length === 0 && !loadingSchedules()) {
            <p class="text-muted-foreground text-sm">No schedules yet.</p>
          } @else {
            <div hlmTableContainer>
              <table hlmTable>
                <thead hlmTableHeader>
                  <tr hlmTableRow>
                    <th hlmTableHead>State</th>
                    <th hlmTableHead>Instance</th>
                    <th hlmTableHead>Description</th>
                    <th hlmTableHead>Cron (UTC)</th>
                    <th hlmTableHead>Next run</th>
                    <th hlmTableHead>Last run</th>
                    <th hlmTableHead class="w-28 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody hlmTableBody>
                  @for (s of filteredSchedules(); track s.id) {
                    <tr hlmTableRow>
                      <td hlmTableCell>
                        @if (s.enabled) {
                          <span hlmBadge variant="default">Active</span>
                        } @else {
                          <span hlmBadge variant="secondary">Paused</span>
                        }
                        @if (s.lastStatus === 'error') {
                          <span hlmTooltip="Last run failed">
                            <ng-icon name="lucideCircleAlert" size="14" class="text-destructive ml-1 inline" />
                          </span>
                        }
                      </td>
                      <td hlmTableCell class="font-mono text-xs">{{ instanceNameFor(s.instanceId) }}</td>
                      <td hlmTableCell class="text-sm">{{ s.description }}</td>
                      <td hlmTableCell class="font-mono text-xs">{{ s.cronExpression }}</td>
                      <td hlmTableCell class="text-muted-foreground font-mono text-xs">
                        {{ s.nextRunAt ? (s.nextRunAt | date: 'yyyy-MM-dd HH:mm') + ' UTC' : ' - ' }}
                      </td>
                      <td hlmTableCell class="text-muted-foreground font-mono text-xs">
                        {{ s.lastRunAt ? (s.lastRunAt | date: 'yyyy-MM-dd HH:mm') + ' UTC' : 'never' }}
                      </td>
                      <td hlmTableCell class="text-right">
                        <div class="flex items-center justify-end gap-1">
                          <button
                            hlmBtn
                            variant="ghost"
                            size="icon"
                            type="button"
                            [attr.aria-label]="s.enabled ? 'Pause schedule' : 'Resume schedule'"
                            [hlmTooltip]="s.enabled ? 'Pause' : 'Resume'"
                            (click)="toggleSchedule(s)"
                          >
                            <ng-icon [name]="s.enabled ? 'lucidePause' : 'lucidePlay'" size="14" />
                          </button>
                          <button
                            hlmBtn
                            variant="ghost"
                            size="icon"
                            type="button"
                            aria-label="Delete schedule"
                            hlmTooltip="Delete schedule"
                            (click)="deleteSchedule(s)"
                          >
                            <ng-icon name="lucideTrash2" size="14" />
                          </button>
                        </div>
                      </td>
                    </tr>
                    @if (s.lastError) {
                      <tr hlmTableRow>
                        <td hlmTableCell [attr.colspan]="7" class="text-destructive bg-destructive/5 text-xs">
                          Last error: {{ s.lastError }}
                        </td>
                      </tr>
                    }
                  }
                </tbody>
              </table>
            </div>
          }
        </div>
      </section>

      <section hlmCard class="flex min-h-0 flex-col">
        <div hlmCardHeader>
          <h2 hlmCardTitle>Backups</h2>
          <p hlmCardDescription>
            @if (selectedInstance()) {
              Snapshots for the selected instance.
            } @else {
              Snapshots across every instance.
            }
          </p>
        </div>

        <div hlmCardContent class="min-h-0 flex-1 overflow-auto">
          @if (filteredEntries().length === 0 && !loading()) {
            <p class="text-muted-foreground text-sm">No backups yet.</p>
          } @else {
            <div hlmTableContainer>
              <table hlmTable>
                <thead hlmTableHeader>
                  <tr hlmTableRow>
                    <th hlmTableHead>Created</th>
                    <th hlmTableHead>Engine</th>
                    <th hlmTableHead>Instance</th>
                    <th hlmTableHead>File</th>
                    <th hlmTableHead class="text-right">Size</th>
                    <th hlmTableHead class="w-32 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody hlmTableBody>
                  @for (e of filteredEntries(); track e.id) {
                    <tr hlmTableRow>
                      <td hlmTableCell class="font-mono text-xs">{{ e.createdAt | date: 'yyyy-MM-dd HH:mm:ss' }}</td>
                      <td hlmTableCell>
                        <span class="inline-flex items-center gap-2 leading-none">
                          <ng-icon [name]="engineIcon(e.engine)" size="16" class="shrink-0" />
                          <span class="text-sm">{{ e.engine }}</span>
                        </span>
                      </td>
                      <td hlmTableCell class="font-mono text-xs">{{ instanceNameFor(e.instanceId) }}</td>
                      <td hlmTableCell class="font-mono text-xs">{{ e.fileName }}</td>
                      <td hlmTableCell class="text-muted-foreground text-right font-mono text-xs">{{ humanSize(e.sizeBytes) }}</td>
                      <td hlmTableCell class="text-right">
                        <div class="flex items-center justify-end gap-1">
                          <button
                            hlmBtn
                            variant="ghost"
                            size="icon"
                            type="button"
                            aria-label="Download backup"
                            hlmTooltip="Download"
                            (click)="download(e)"
                          >
                            <ng-icon name="lucideDownload" size="16" />
                          </button>
                          <button
                            hlmBtn
                            variant="ghost"
                            size="icon"
                            type="button"
                            aria-label="Restore from this backup"
                            hlmTooltip="Restore"
                            [disabled]="restoring() === e.id"
                            (click)="restore(e)"
                          >
                            <ng-icon name="lucideHistory" size="16" />
                          </button>
                          <button
                            hlmBtn
                            variant="ghost"
                            size="icon"
                            type="button"
                            aria-label="Delete backup"
                            hlmTooltip="Delete"
                            [disabled]="deleting() === e.id"
                            (click)="remove(e)"
                          >
                            <ng-icon name="lucideTrash2" size="16" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }

          @if (error(); as err) {
            <p class="text-destructive mt-3 text-sm">{{ err }}</p>
          }
        </div>
      </section>
      </div>
      </main>
    </section>
  `,
})
export class Backups {
  private readonly api = inject(BackupsService);
  private readonly dbApi = inject(DatabaseService);
  private readonly http = inject(HttpClient);
  private readonly confirmService = inject(ConfirmService);
  private readonly scheduleDialog = inject(BackupScheduleDialogService);

  protected readonly entries = signal<ReadonlyArray<BackupEntryDto>>([]);
  protected readonly instances = signal<ReadonlyArray<DatabaseInstanceDto>>([]);
  protected readonly schedules = signal<ReadonlyArray<BackupScheduleDto>>([]);
  protected readonly loading = signal(false);
  protected readonly loadingSchedules = signal(false);
  protected readonly creating = signal(false);
  protected readonly restoring = signal<string | null>(null);
  protected readonly deleting = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly selectedInstanceId = signal<string | null>(null);

  protected readonly instanceById = computed(() =>
    new Map(this.instances().map((i) => [i.id, i] as const)),
  );

  protected readonly selectedInstance = computed(() =>
    this.instanceById().get(this.selectedInstanceId() ?? '') ?? null,
  );

  // Schedules + entries filter down to the selected instance when one is chosen, otherwise
  // they stay global - lets you sweep all backups across instances when nothing is picked.
  protected readonly filteredSchedules = computed(() => {
    const id = this.selectedInstanceId();
    return id ? this.schedules().filter((s) => s.instanceId === id) : this.schedules();
  });

  protected readonly filteredEntries = computed(() => {
    const id = this.selectedInstanceId();
    return id ? this.entries().filter((e) => e.instanceId === id) : this.entries();
  });

  protected instanceUrl(i: DatabaseInstanceDto): string {
    return `${i.host}:${i.port}/${i.databaseName}`;
  }

  protected reloadInstances(): void {
    this.dbApi.apiDatabaseGet().subscribe({
      next: (list) => this.instances.set(list),
      error: (err) => this.error.set(messageOf(err) ?? 'Failed to load instances'),
    });
  }

  constructor() {
    this.reload();
    this.loadInstances();
    this.loadSchedules();
  }

  private loadSchedules(): void {
    this.loadingSchedules.set(true);
    this.api.apiBackupsSchedulesGet().subscribe({
      next: (list) => { this.schedules.set(list); this.loadingSchedules.set(false); },
      error: (err) => { this.error.set(messageOf(err) ?? 'Failed to load schedules'); this.loadingSchedules.set(false); },
    });
  }

  protected async openScheduleDialog(): Promise<void> {
    const created = await this.scheduleDialog.open({
      instances: this.instances(),
      preselectedInstanceId: this.selectedInstanceId(),
    });
    if (created) {
      this.schedules.update((curr) => [created, ...curr]);
    }
  }

  protected toggleSchedule(s: BackupScheduleDto): void {
    this.api.apiBackupsSchedulesIdPatch(s.id, { enabled: !s.enabled }).subscribe({
      next: (updated) => {
        this.schedules.update((curr) => curr.map((x) => (x.id === updated.id ? updated : x)));
      },
      error: (err) => this.error.set(messageOf(err) ?? 'Failed to toggle schedule'),
    });
  }

  protected async deleteSchedule(s: BackupScheduleDto): Promise<void> {
    const ok = await this.confirmService.open({
      title: 'Delete schedule?',
      message: `${s.description} will no longer run. Existing backups are kept.`,
      confirmLabel: 'Delete schedule',
      destructive: true,
    });
    if (!ok) return;
    this.api.apiBackupsSchedulesIdDelete(s.id).subscribe({
      next: () => this.schedules.update((curr) => curr.filter((x) => x.id !== s.id)),
      error: (err) => this.error.set(messageOf(err) ?? 'Failed to delete schedule'),
    });
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.apiBackupsGet().subscribe({
      next: (entries) => {
        this.entries.set(entries);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(messageOf(err) ?? 'Failed to load backups');
        this.loading.set(false);
      },
    });
  }

  private loadInstances(): void {
    this.dbApi.apiDatabaseGet().subscribe({
      next: (list) => this.instances.set(list),
      error: (err) => this.error.set(messageOf(err) ?? 'Failed to load instances'),
    });
  }

  protected create(): void {
    const id = this.selectedInstanceId();
    if (!id) return;
    this.creating.set(true);
    this.error.set(null);
    this.api.apiBackupsInstanceInstanceIdPost(id).subscribe({
      next: (entry) => {
        this.entries.update((curr) => [entry, ...curr]);
        this.creating.set(false);
      },
      error: (err) => {
        this.error.set(messageOf(err) ?? 'Backup failed');
        this.creating.set(false);
      },
    });
  }

  protected download(entry: BackupEntryDto): void {
    // The generated client returns a string for binary endpoints; fetch as blob directly to
    // preserve gzip dumps. Trigger a regular browser download via an anchor.
    const url = `${environment.apiBaseUrl}/api/Backups/${entry.id}`;
    this.http.get(url, { responseType: 'blob' }).subscribe({
      next: (blob) => {
        const objectUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = objectUrl;
        a.download = entry.fileName;
        a.click();
        URL.revokeObjectURL(objectUrl);
      },
      error: (err) => this.error.set(messageOf(err) ?? 'Download failed'),
    });
  }

  protected async restore(entry: BackupEntryDto): Promise<void> {
    const inst = this.instanceById().get(entry.instanceId);
    const label = inst ? `${inst.engine} - ${inst.containerName}` : entry.instanceId;
    const ok = await this.confirmService.open({
      title: `Restore into ${label}?`,
      message: `${entry.fileName} will overwrite all current data on that instance. This cannot be undone.`,
      confirmLabel: 'Restore',
      destructive: true,
    });
    if (!ok) return;

    this.restoring.set(entry.id);
    this.error.set(null);
    this.api.apiBackupsIdRestorePost(entry.id).subscribe({
      next: () => this.restoring.set(null),
      error: (err) => {
        this.error.set(messageOf(err) ?? 'Restore failed');
        this.restoring.set(null);
      },
    });
  }

  protected async remove(entry: BackupEntryDto): Promise<void> {
    const ok = await this.confirmService.open({
      title: `Delete backup?`,
      message: `${entry.fileName} will be removed from disk. This cannot be undone.`,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    this.deleting.set(entry.id);
    this.error.set(null);
    this.api.apiBackupsIdDelete(entry.id).subscribe({
      next: () => {
        this.entries.update((curr) => curr.filter((e) => e.id !== entry.id));
        this.deleting.set(null);
      },
      error: (err) => {
        this.error.set(messageOf(err) ?? 'Delete failed');
        this.deleting.set(null);
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
      case 'pgvector':    return 'simplePostgresql';
      case 'neo4j':       return 'simpleNeo4j';
      case 'qdrant':      return 'customQdrant';
      case 'valkey':      return 'customValkey';
      case 'mssql':       return 'customMssql';
      default:         return 'lucideDatabase';
    }
  }

  protected instanceNameFor(id: string): string {
    return this.instanceById().get(id)?.containerName ?? id;
  }

  protected readonly instanceLabel = (id: string): string => {
    const i = this.instanceById().get(id);
    return i ? `${i.engine} - ${i.containerName}` : id;
  };

  protected humanSize(sizeBytes: unknown): string {
    const n = typeof sizeBytes === 'number' ? sizeBytes : Number(sizeBytes ?? 0);
    if (!Number.isFinite(n) || n <= 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let i = 0;
    let v = n;
    while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
    return `${v.toFixed(v < 10 && i > 0 ? 1 : 0)} ${units[i]}`;
  }
}

function messageOf(err: unknown): string | null {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  if (err instanceof Error) return err.message;
  return null;
}

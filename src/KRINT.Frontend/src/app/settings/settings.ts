import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBrain, lucideCheck, lucideCircleAlert, lucideDatabase } from '@ng-icons/lucide';
import { simpleApachecassandra, simpleApachecouchdb, simpleClickhouse, simpleCockroachlabs, simpleElasticsearch, simpleMariadb, simpleMongodb, simpleMysql, simpleNeo4j, simplePostgresql, simpleRedis, simpleTimescale } from '@ng-icons/simple-icons';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { customMssql, customQdrant, customValkey } from '../shared/icons/custom-icons';
import { SettingsService } from '../api/api/settings.service';
import { SettingsDto } from '../api/model/settingsDto';

@Component({
  selector: 'app-settings',
  imports: [
    ContentHeader,
    NgIcon,
    HlmBadgeImports,
    HlmCardImports,
    HlmTableImports,
  ],
  providers: [
    provideIcons({
      lucideBrain,
      lucideCheck,
      lucideCircleAlert,
      lucideDatabase,
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
    <!-- Fills the viewport. The two big cards (Port ranges + Supported engines) split the
         available height; Vault stays auto-sized at the bottom. Each big card scrolls its
         own body so the page itself never scrolls. -->
    <section class="flex h-[calc(100vh-3rem)] flex-col gap-4 border-t p-4">
      @if (settings(); as s) {
        <section hlmCard class="flex min-h-0 flex-1 flex-col">
          <div hlmCardHeader>
            <h2 hlmCardTitle>Port ranges</h2>
            <p hlmCardDescription>
              Host ports KRINT will allocate from per engine. Configured in <code class="font-mono">krint.yaml</code>.
            </p>
          </div>
          <div hlmCardContent class="min-h-0 flex-1 overflow-auto">
            <div hlmTableContainer>
              <table hlmTable>
                <thead hlmTableHeader>
                  <tr hlmTableRow>
                    <th hlmTableHead>Engine</th>
                    <th hlmTableHead class="text-right">Start</th>
                    <th hlmTableHead class="text-right">End</th>
                  </tr>
                </thead>
                <tbody hlmTableBody>
                  @for (r of s.portRanges; track r.engine) {
                    <tr hlmTableRow>
                      <td hlmTableCell>
                        <span class="inline-flex items-center gap-3 leading-none">
                          <ng-icon [name]="engineIcon(r.engine)" size="18" class="shrink-0" />
                          <span class="text-sm">{{ r.engine }}</span>
                        </span>
                      </td>
                      <td hlmTableCell class="text-right font-mono">{{ r.start }}</td>
                      <td hlmTableCell class="text-right font-mono">{{ r.end }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </div>
        </section>

        <section hlmCard class="flex min-h-0 flex-1 flex-col">
          <div hlmCardHeader>
            <h2 hlmCardTitle>Supported engines</h2>
            <p hlmCardDescription>Engines this instance of KRINT can provision, plus available versions.</p>
          </div>
          <div hlmCardContent class="min-h-0 flex-1 overflow-auto">
            <ul class="divide-border divide-y rounded-md border">
              @for (e of s.supportedEngines; track e.key) {
                <li class="grid grid-cols-[auto_1fr_auto] items-center gap-3 px-3 py-2 text-sm">
                  <ng-icon [name]="engineIcon(e.key)" size="18" />
                  <span class="font-medium">{{ e.displayName }}</span>
                  <span class="text-muted-foreground font-mono text-xs">{{ e.versions.join(' · ') }}</span>
                </li>
              }
            </ul>
          </div>
        </section>

        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>Vault</h2>
            <p hlmCardDescription>Master key for AES-GCM encryption of stored secrets.</p>
          </div>
          <div hlmCardContent class="flex items-center gap-2 text-sm">
            @if (s.vaultMasterKeyConfigured) {
              <ng-icon name="lucideCheck" class="text-primary" size="18" />
              <span>Master key configured.</span>
            } @else {
              <ng-icon name="lucideCircleAlert" class="text-destructive" size="18" />
              <span class="text-destructive">Master key missing - secrets cannot be stored.</span>
            }
          </div>
        </section>
      } @else if (error(); as err) {
        <p class="text-destructive text-sm">{{ err }}</p>
      } @else {
        <p class="text-muted-foreground text-sm">Loading…</p>
      }
    </section>
  `,
})
export class Settings {
  private readonly api = inject(SettingsService);

  protected readonly settings = signal<SettingsDto | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.apiSettingsGet().subscribe({
      next: (s) => this.settings.set(s),
      error: (err) => this.error.set(err instanceof Error ? err.message : 'Failed to load settings'),
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
      default:         return 'simplePostgresql';
    }
  }
}

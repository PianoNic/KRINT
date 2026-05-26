import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowUpCircle, lucideBrain, lucideDatabase, lucideEllipsisVertical, lucideEye, lucideGlobe, lucideLink, lucideLock, lucidePencil, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { simpleApachecassandra, simpleApachecouchdb, simpleClickhouse, simpleCockroachlabs, simpleElasticsearch, simpleMariadb, simpleMongodb, simpleMysql, simpleNeo4j, simplePostgresql, simpleRedis, simpleTimescale } from '@ng-icons/simple-icons';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { customMssql, customQdrant, customValkey } from '../shared/icons/custom-icons';
import { DatabaseInstanceDto } from '../api/model/databaseInstanceDto';
import { DatabasesStore } from '../shared/stores/databases.store';
import { DatabaseDetailsDialog } from './database-details-dialog';
import { DatabaseEditDialog } from './database-edit-dialog';
import { DatabaseRegisterExternalDialog } from './database-register-external-dialog';
import { DatabaseUpgradeDialog } from './database-upgrade-dialog';

@Component({
  selector: 'app-databases',
  imports: [
    ContentHeader,
    RouterLink,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmDropdownMenuImports,
    HlmTableImports,
    HlmTooltipImports,
  ],
  providers: [
    provideIcons({
      lucideArrowUpCircle,
      lucideBrain,
      lucideDatabase,
      lucideEllipsisVertical,
      lucideEye,
      lucideGlobe,
      lucideLink,
      lucideLock,
      lucidePencil,
      lucidePlus,
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
  templateUrl: './databases.html',
})
export class Databases {
  protected readonly store = inject(DatabasesStore);
  private readonly dialog = inject(HlmDialogService);
  private readonly confirmService = inject(ConfirmService);

  protected viewDetails(id: string): void {
    this.dialog.open(DatabaseDetailsDialog, {
      context: { id },
      contentClass: 'sm:max-w-[560px]',
    });
  }

  protected editInstance(db: DatabaseInstanceDto): void {
    this.dialog.open(DatabaseEditDialog, {
      context: { id: db.id, engine: db.engine, containerName: db.containerName ?? db.displayName, displayName: db.displayName },
      contentClass: 'sm:max-w-[640px]',
    });
  }

  protected registerExternal(): void {
    this.dialog.open(DatabaseRegisterExternalDialog, {
      contentClass: 'sm:max-w-[640px]',
    });
  }

  protected upgradeInstance(db: DatabaseInstanceDto): void {
    // Upgrade is dump-restore-swap and creates a fresh container under a new name. For
    // externals (managed by docker compose or similar), that would diverge from the user's
    // declared state - so we gate it on IsManaged, not on container presence.
    if (!db.isManaged || !db.containerName) return;
    this.dialog.open(DatabaseUpgradeDialog, {
      context: { id: db.id, engine: db.engine, containerName: db.containerName, currentVersion: db.version },
      contentClass: 'sm:max-w-[480px]',
    });
  }

  protected async toggleVisibility(db: DatabaseInstanceDto): Promise<void> {
    if (!db.isManaged || !db.containerName) return;
    const next = !db.isPublic;
    const ok = await this.confirmService.open({
      title: next
        ? `Expose ${db.displayName} on the LAN?`
        : `Restrict ${db.displayName} to localhost?`,
      message:
        'The container will be stopped and recreated in place. The data volume is preserved, but active connections will drop for a few seconds.',
      confirmLabel: next ? 'Expose publicly' : 'Lock to localhost',
      destructive: next, // exposing on the LAN is the "louder" action - mark it destructive for the red confirm.
    });
    if (!ok) return;
    this.store.setVisibility({ id: db.id, dto: { isPublic: next } });
  }

  protected async deleteInstance(db: DatabaseInstanceDto): Promise<void> {
    const ok = await this.confirmService.open({
      title: db.isManaged
        ? `Delete instance ${db.containerName ?? db.displayName}?`
        : `Forget external database ${db.displayName}?`,
      message: db.isManaged
        ? 'This stops the container, removes its volume, and clears the secret. The data cannot be recovered.'
        : 'KRINT will forget this external database and clear its stored credentials. The remote database itself is not touched.',
      confirmLabel: db.isManaged ? 'Delete instance' : 'Forget database',
      destructive: true,
    });
    if (!ok) return;
    this.store.deleteInstance(db.id);
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

  protected reload(): void {
    this.store.load();
  }
}

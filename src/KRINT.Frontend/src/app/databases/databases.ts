import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBrain, lucideDatabase, lucideEllipsisVertical, lucideEye, lucidePencil, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
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
      lucideBrain,
      lucideDatabase,
      lucideEllipsisVertical,
      lucideEye,
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
      context: { id: db.id, engine: db.engine, containerName: db.containerName },
      contentClass: 'sm:max-w-[640px]',
    });
  }

  protected async deleteInstance(db: DatabaseInstanceDto): Promise<void> {
    const ok = await this.confirmService.open({
      title: `Delete instance ${db.containerName}?`,
      message: 'This stops the container, removes its volume, and clears the secret. The data cannot be recovered.',
      confirmLabel: 'Delete instance',
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

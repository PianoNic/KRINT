import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { TitleCasePipe } from '@angular/common';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideArrowRight,
  lucideBrain,
  lucideCheck,
  lucideDatabase,
  lucideKeyRound,
  lucidePlus,
  lucideRocket,
  lucideTrash2,
} from '@ng-icons/lucide';
import { simpleApachecassandra, simpleApachecouchdb, simpleClickhouse, simpleCockroachlabs, simpleElasticsearch, simpleMariadb, simpleMongodb, simpleMysql, simpleNeo4j, simplePostgresql, simpleRedis, simpleTimescale } from '@ng-icons/simple-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { CopyButton } from '../shared/components/copy-button/copy-button';
import { customAzurite, customMssql, customQdrant, customSeaweedfs, customValkey } from '../shared/icons/custom-icons';
import { DatabaseService } from '../api/api/database.service';
import { SupportedDatabaseDto } from '../api/model/supportedDatabaseDto';
import { ProvisionResultDto } from '../api/model/provisionResultDto';

type WizardUser = { name: string; grantDatabases: string[] };

@Component({
  selector: 'app-create',
  imports: [
    ContentHeader,
    CopyButton,
    NgIcon,
    HlmButtonImports,
    HlmCardImports,
    HlmCheckboxImports,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
    HlmTooltipImports,
    TitleCasePipe,
  ],
  providers: [
    provideIcons({
      lucideArrowLeft,
      lucideArrowRight,
      lucideBrain,
      lucideCheck,
      lucideDatabase,
      lucideKeyRound,
      lucidePlus,
      lucideRocket,
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
      customAzurite,
      customSeaweedfs,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './create.html',
})
export class Create {
  private readonly api = inject(DatabaseService);
  private readonly router = inject(Router);

  // ----- step state -----
  protected readonly step = signal<1 | 2 | 3 | 4 | 5 | 6>(1);

  // ----- form state -----
  protected readonly engine = signal<string | null>(null);
  protected readonly version = signal<string | null>(null);
  protected readonly displayName = signal('');
  protected readonly defaultDbName = signal('');
  protected readonly databases = signal<string[]>([]);
  protected readonly users = signal<WizardUser[]>([]);
  protected readonly selectedPlugins = signal<ReadonlySet<string>>(new Set());
  protected readonly isPublic = signal(false);
  // Root password: empty string = auto-generate at provision time.
  protected readonly customRootPassword = signal('');
  // Per-user custom passwords - empty string at index n means auto-generate.
  protected readonly userPasswords = signal<string[]>([]);

  // ----- async state -----
  protected readonly supported = signal<ReadonlyArray<SupportedDatabaseDto>>([]);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<ProvisionResultDto | null>(null);

  // ----- computed -----
  protected readonly versions = computed(
    () => this.supported().find((s) => s.key === this.engine())?.versions ?? [],
  );

  protected readonly capabilities = computed(
    () => this.supported().find((s) => s.key === this.engine())?.capabilities ?? null,
  );

  // Engine-aware term shortcuts. Mongo calls them "collections"; Qdrant calls them
  // "collections" + "points"; Redis has "keys"; etc. The catalog already declares these
  // strings on EngineCapabilitiesDto - the wizard just plucks them.
  protected readonly databaseTerm = computed(() => this.capabilities()?.databaseTerm ?? 'database');
  protected readonly tableTerm    = computed(() => this.capabilities()?.tableTerm ?? 'table');
  protected readonly databaseTermPlural = computed(() => pluralize(this.databaseTerm()));
  protected readonly tableTermPlural    = computed(() => pluralize(this.tableTerm()));

  // Whether the engine supports a user-named default database. Single-keyspace engines
  // (Qdrant, Elasticsearch, Redis) hide the Basics > Default DB input and the Databases
  // step entirely - there's nothing meaningful for the user to type.
  protected readonly supportsDatabaseNaming = computed(() => this.capabilities()?.supportsCreateDatabase ?? true);

  protected readonly steps = computed<ReadonlyArray<{ n: 1 | 2 | 3 | 4 | 5 | 6; label: string }>>(() => {
    const term = capitalize(this.databaseTermPlural());
    return [
      { n: 1, label: 'Engine' },
      { n: 2, label: 'Basics' },
      { n: 3, label: 'Plugins' },
      // Relabel to match the engine: "Databases" / "Collections" / "Indexes" / "Buckets".
      { n: 4, label: term },
      { n: 5, label: 'Users' },
      { n: 6, label: 'Review' },
    ];
  });

  protected readonly availablePlugins = computed(
    () => this.supported().find((s) => s.key === this.engine())?.plugins ?? [],
  );

  protected readonly hasPlugins = computed(() => this.availablePlugins().length > 0);

  protected readonly selectedPluginKeys = computed(() => Array.from(this.selectedPlugins()));

  protected readonly defaultDbPlaceholder = computed(() => {
    switch (this.engine()) {
      case 'postgres': return 'postgres';
      case 'mysql':    return 'mysql';
      case 'mongo':    return 'admin';
      default:         return 'default';
    }
  });

  protected readonly allDbNames = computed(() => {
    const def = this.defaultDbName().trim() || this.defaultDbPlaceholder();
    return [def, ...this.databases().filter((d) => d.trim() !== '')];
  });

  // Server rule (DatabaseNameValidator): start with letter/underscore, then [A-Za-z0-9_-], max 63 chars.
  private static readonly NAME_RE = /^[A-Za-z_][A-Za-z0-9_-]{0,62}$/;
  // Matches SafePasswordGuard on the backend - the alphabet KRINT can safely inline into DDL.
  private static readonly PASSWORD_RE = /^[A-Za-z0-9\-_.~]+$/;

  protected passwordError(value: string): string | null {
    const v = value.trim();
    if (v === '') return null; // empty = auto-generate
    if (!Create.PASSWORD_RE.test(v)) {
      return 'Allowed characters: A-Z, a-z, 0-9, and - _ . ~';
    }
    return null;
  }

  protected nameError(value: string, { required }: { required: boolean }): string | null {
    const trimmed = value.trim();
    if (trimmed === '') return required ? 'Required.' : null;
    if (trimmed.length > 63) return 'Must be 63 characters or fewer.';
    if (!Create.NAME_RE.test(trimmed)) {
      return 'Must start with a letter or underscore and contain only A-Z, a-z, 0-9, _ or -.';
    }
    return null;
  }

  protected readonly defaultDbError = computed(() =>
    this.nameError(this.defaultDbName(), { required: false }),
  );
  // Display name is freeform - just length-check it. People want to put "Pangolin" or
  // "Acme staging" in there, not snake_case identifiers.
  protected readonly displayNameError = computed(() => {
    const v = this.displayName().trim();
    if (v === '') return 'Required.';
    if (v.length > 64) return 'Must be 64 characters or fewer.';
    return null;
  });
  protected readonly databaseErrors = computed(() =>
    this.databases().map((d) => this.nameError(d, { required: true })),
  );
  protected readonly userNameErrors = computed(() =>
    this.users().map((u) => this.nameError(u.name, { required: true })),
  );
  protected readonly rootPasswordError = computed(() => this.passwordError(this.customRootPassword()));
  protected readonly userPasswordErrors = computed(() =>
    this.userPasswords().map((p) => this.passwordError(p)),
  );

  protected readonly canNext = computed(() => {
    switch (this.step()) {
      case 1: return !!this.engine();
      case 2: return !!this.version() && !this.displayNameError() && (!this.supportsDatabaseNaming() || !this.defaultDbError()) && !this.rootPasswordError();
      case 3: return true;  // Plugins are always optional
      case 4: return !this.supportsDatabaseNaming() || this.databaseErrors().every((e) => e === null);
      case 5: return this.userNameErrors().every((e) => e === null) && this.userPasswordErrors().every((e) => e === null);
      default: return true;
    }
  });

  constructor() {
    this.api.apiDatabaseSupportedGet().subscribe({
      next: (s) => this.supported.set(s),
      error: (err) => this.error.set(messageOf(err)),
    });

    effect(() => {
      // Reset version + plugins when engine changes.
      this.engine();
      this.version.set(null);
      this.selectedPlugins.set(new Set());
    });
  }

  protected togglePlugin(key: string): void {
    this.selectedPlugins.update((curr) => {
      const next = new Set(curr);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }

  protected isPluginSelected(key: string): boolean {
    return this.selectedPlugins().has(key);
  }

  protected engineIcon(key: string): string {
    switch (key) {
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
      case 'seaweedfs':       return 'customSeaweedfs';
      case 'azurite':       return 'customAzurite';
      default:         return 'lucideDatabase';
    }
  }

  protected selectEngine(key: string): void {
    this.engine.set(key);
  }

  // No more auto-skip: every step is visited even when there's nothing to configure for
  // this engine. Each step's template surfaces an "N/A for this engine" empty state so the
  // wizard reads predictably rather than appearing to jump.
  protected next(): void {
    if (!this.canNext()) return;
    const s = this.step();
    if (s >= 6) return;
    this.step.set((s + 1) as 2 | 3 | 4 | 5 | 6);
  }

  protected back(): void {
    const s = this.step();
    if (s <= 1) return;
    this.step.set((s - 1) as 1 | 2 | 3 | 4 | 5);
  }

  // ----- databases step -----
  protected addDatabase(): void {
    this.databases.update((list) => [...list, '']);
  }

  protected updateDatabase(index: number, value: string): void {
    this.databases.update((list) => list.map((v, i) => (i === index ? value : v)));
  }

  protected removeDatabase(index: number): void {
    const name = this.databases()[index];
    this.databases.update((list) => list.filter((_, i) => i !== index));
    // remove any user grants pointing at this name
    this.users.update((users) =>
      users.map((u) => ({ ...u, grantDatabases: u.grantDatabases.filter((g) => g !== name) })),
    );
  }

  // ----- users step -----
  protected addUser(): void {
    this.users.update((list) => [...list, { name: '', grantDatabases: [] }]);
    // Keep the parallel passwords array in sync so [index] indexing stays consistent.
    this.userPasswords.update((list) => [...list, '']);
  }

  protected updateUserName(index: number, value: string): void {
    this.users.update((list) => list.map((u, i) => (i === index ? { ...u, name: value } : u)));
  }

  protected updateUserPassword(index: number, value: string): void {
    this.userPasswords.update((list) => list.map((p, i) => (i === index ? value : p)));
  }

  protected toggleUserGrant(index: number, db: string, checked: boolean): void {
    this.users.update((list) =>
      list.map((u, i) =>
        i !== index
          ? u
          : {
              ...u,
              grantDatabases: checked
                ? Array.from(new Set([...u.grantDatabases, db]))
                : u.grantDatabases.filter((g) => g !== db),
            },
      ),
    );
  }

  protected removeUser(index: number): void {
    this.users.update((list) => list.filter((_, i) => i !== index));
    this.userPasswords.update((list) => list.filter((_, i) => i !== index));
  }

  protected userHasGrant(index: number, db: string): boolean {
    return this.users()[index]?.grantDatabases.includes(db) ?? false;
  }

  // ----- submit -----
  protected launch(): void {
    if (!this.engine() || !this.version()) return;
    this.submitting.set(true);
    this.error.set(null);

    const payload = {
      engine: this.engine()!,
      version: this.version()!,
      displayName: this.displayName().trim(),
      defaultDatabaseName: this.defaultDbName().trim() || null,
      databases: this.databases().map((d) => d.trim()).filter((d) => d !== ''),
      users: this.users().map((u, i) => ({
        name: u.name.trim(),
        grantDatabases: u.grantDatabases,
        password: (this.userPasswords()[i] ?? '').trim() || null,
      })),
      plugins: Array.from(this.selectedPlugins()),
      isPublic: this.isPublic(),
      password: this.customRootPassword().trim() || null,
    };

    this.api.apiDatabaseProvisionPost(payload).subscribe({
      next: (res) => {
        this.result.set(res);
        this.submitting.set(false);
      },
      error: (err) => {
        this.error.set(messageOf(err));
        this.submitting.set(false);
      },
    });
  }

  protected goToInstances(): void {
    this.router.navigateByUrl('/instances');
  }
}

// Tiny English pluralizer - enough for the catalog's terms (database, collection, index,
// bucket, namespace, keyspace, document, point, key, org). We deliberately don't bring in
// a real i18n library here.
function pluralize(term: string): string {
  if (term.endsWith('y')) return term.slice(0, -1) + 'ies';
  if (term.endsWith('x') || term.endsWith('s') || term.endsWith('ch') || term.endsWith('sh')) return term + 'es';
  return term + 's';
}

function capitalize(s: string): string {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  if (err instanceof Error) return err.message;
  return 'Request failed';
}

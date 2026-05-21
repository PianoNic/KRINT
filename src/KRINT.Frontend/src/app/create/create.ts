import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideArrowRight,
  lucideCheck,
  lucideDatabase,
  lucideKeyRound,
  lucidePlus,
  lucideRocket,
  lucideTrash2,
} from '@ng-icons/lucide';
import { simpleMariadb, simpleMongodb, simpleMysql, simplePostgresql } from '@ng-icons/simple-icons';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { CopyButton } from '../shared/components/copy-button/copy-button';
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
  ],
  providers: [
    provideIcons({
      lucideArrowLeft,
      lucideArrowRight,
      lucideCheck,
      lucideDatabase,
      lucideKeyRound,
      lucidePlus,
      lucideRocket,
      lucideTrash2,
      simplePostgresql,
      simpleMysql,
      simpleMongodb,
      simpleMariadb,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './create.html',
})
export class Create {
  private readonly api = inject(DatabaseService);
  private readonly router = inject(Router);

  // ----- step state -----
  protected readonly step = signal<1 | 2 | 3 | 4 | 5>(1);
  protected readonly steps: ReadonlyArray<{ n: 1 | 2 | 3 | 4 | 5; label: string }> = [
    { n: 1, label: 'Engine' },
    { n: 2, label: 'Basics' },
    { n: 3, label: 'Databases' },
    { n: 4, label: 'Users' },
    { n: 5, label: 'Review' },
  ];

  // ----- form state -----
  protected readonly engine = signal<string | null>(null);
  protected readonly version = signal<string | null>(null);
  protected readonly defaultDbName = signal('');
  protected readonly databases = signal<string[]>([]);
  protected readonly users = signal<WizardUser[]>([]);

  // ----- async state -----
  protected readonly supported = signal<ReadonlyArray<SupportedDatabaseDto>>([]);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<ProvisionResultDto | null>(null);

  // ----- computed -----
  protected readonly versions = computed(
    () => this.supported().find((s) => s.key === this.engine())?.versions ?? [],
  );

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

  protected readonly canNext = computed(() => {
    switch (this.step()) {
      case 1: return !!this.engine();
      case 2: return !!this.version();
      case 3: return this.databases().every((d) => d.trim() !== '');
      case 4: return this.users().every((u) => u.name.trim() !== '');
      default: return true;
    }
  });

  constructor() {
    this.api.apiDatabaseSupportedGet().subscribe({
      next: (s) => this.supported.set(s),
      error: (err) => this.error.set(messageOf(err)),
    });

    effect(() => {
      // Reset version when engine changes.
      this.engine();
      this.version.set(null);
    });
  }

  protected engineIcon(key: string): string {
    switch (key) {
      case 'postgres': return 'simplePostgresql';
      case 'mysql':    return 'simpleMysql';
      case 'mongo':    return 'simpleMongodb';
      case 'mariadb':  return 'simpleMariadb';
      default:         return 'lucideDatabase';
    }
  }

  protected selectEngine(key: string): void {
    this.engine.set(key);
  }

  protected next(): void {
    if (!this.canNext()) return;
    const s = this.step();
    if (s < 5) this.step.set((s + 1) as 2 | 3 | 4 | 5);
  }

  protected back(): void {
    const s = this.step();
    if (s > 1) this.step.set((s - 1) as 1 | 2 | 3 | 4);
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
  }

  protected updateUserName(index: number, value: string): void {
    this.users.update((list) => list.map((u, i) => (i === index ? { ...u, name: value } : u)));
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
      defaultDatabaseName: this.defaultDbName().trim() || null,
      databases: this.databases().map((d) => d.trim()).filter((d) => d !== ''),
      users: this.users().map((u) => ({
        name: u.name.trim(),
        grantDatabases: u.grantDatabases,
      })),
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

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  if (err instanceof Error) return err.message;
  return 'Request failed';
}

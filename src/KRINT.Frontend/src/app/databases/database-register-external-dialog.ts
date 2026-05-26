import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef } from '@spartan-ng/brain/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideContainer } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { DatabaseService } from '../api/api/database.service';
import { DiscoveredContainerDto } from '../api/model/discoveredContainerDto';
import { DatabasesStore } from '../shared/stores/databases.store';

@Component({
  selector: 'app-database-register-external-dialog',
  imports: [
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
  ],
  providers: [provideIcons({ lucideRefreshCw, lucideContainer })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Register external database</h3>
      <p hlmDialogDescription>
        Connect to a database KRINT didn't provision. We store the credentials so you can browse,
        query, and manage users from the UI. KRINT will <strong>not</strong> own its lifecycle:
        upgrades, backups, and container controls are disabled for external instances.
      </p>
    </hlm-dialog-header>

    <!-- Discover panel. Scans the host's Docker for engine containers KRINT doesn't already
         track, pre-fills creds from env vars where possible. Saves the user from re-typing
         everything when the DB already runs on the same host. -->
    <section class="border-border rounded-md border">
      <header class="flex items-center justify-between gap-2 px-3 py-2 border-b">
        <div class="flex items-center gap-2 text-sm font-medium">
          <ng-icon name="lucideContainer" size="16" />
          Discover Docker containers
        </div>
        <button hlmBtn size="sm" variant="outline" type="button" (click)="scan()" [disabled]="scanning()">
          <ng-icon name="lucideRefreshCw" size="14" [class.animate-spin]="scanning()" />
          {{ scanning() ? 'Scanning…' : 'Scan' }}
        </button>
      </header>

      @if (scanError(); as err) {
        <p class="text-destructive p-3 text-sm">{{ err }}</p>
      } @else if (candidates() === null) {
        <p class="text-muted-foreground p-3 text-sm">
          Click <strong>Scan</strong> to look for database containers running on this Docker host.
        </p>
      } @else if (candidates()!.length === 0) {
        <p class="text-muted-foreground p-3 text-sm">
          No untracked database containers found.
        </p>
      } @else {
        <ul class="divide-border divide-y">
          @for (c of candidates(); track c.containerId) {
            <li class="flex items-center justify-between gap-3 px-3 py-2">
              <div class="min-w-0 flex flex-1 flex-col">
                <span class="inline-flex items-center gap-2 text-sm font-medium">
                  <span class="font-mono truncate">{{ c.containerName }}</span>
                  <span hlmBadge variant="outline" class="text-[10px] uppercase">{{ c.engine }}</span>
                  @if (c.state !== 'running') {
                    <span hlmBadge variant="secondary" class="text-[10px] uppercase">{{ c.state }}</span>
                  }
                  @if (!c.password) {
                    <span hlmBadge variant="secondary" class="text-[10px] normal-case">password needed</span>
                  }
                </span>
                <span class="text-muted-foreground font-mono text-xs">
                  {{ c.image }} · {{ c.host }}:{{ c.port || '?' }}
                </span>
              </div>
              <button hlmBtn size="sm" variant="outline" type="button" (click)="pickCandidate(c)">
                Use
              </button>
            </li>
          }
        </ul>
      }
    </section>

    <div class="grid grid-cols-2 gap-3">
      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="reg-engine" class="text-muted-foreground text-xs uppercase tracking-wide">Engine</label>
        <hlm-select [value]="engine()" (valueChange)="engine.set($event)">
          <hlm-select-trigger id="reg-engine" class="w-full">
            <hlm-select-value placeholder="Pick an engine…" />
          </hlm-select-trigger>
          <hlm-select-content *hlmSelectPortal>
            @for (s of store.supported(); track s.key) {
              <hlm-select-item [value]="s.key">{{ s.displayName }}</hlm-select-item>
            }
          </hlm-select-content>
        </hlm-select>
      </div>

      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="reg-version" class="text-muted-foreground text-xs uppercase tracking-wide">Version</label>
        <input
          hlmInput
          id="reg-version"
          placeholder="e.g. 18.4"
          [value]="version()"
          (input)="version.set($any($event.target).value)"
        />
      </div>

      <div class="col-span-2 flex flex-col gap-1.5">
        <label hlmLabel for="reg-name" class="text-muted-foreground text-xs uppercase tracking-wide">Display name</label>
        <input
          hlmInput
          id="reg-name"
          placeholder="e.g. Prod analytics replica"
          [value]="displayName()"
          (input)="displayName.set($any($event.target).value)"
        />
      </div>

      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="reg-host" class="text-muted-foreground text-xs uppercase tracking-wide">Host</label>
        <input
          hlmInput
          id="reg-host"
          placeholder="db.internal.example.com"
          [value]="host()"
          (input)="host.set($any($event.target).value)"
        />
      </div>

      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="reg-port" class="text-muted-foreground text-xs uppercase tracking-wide">Port</label>
        <input
          hlmInput
          id="reg-port"
          type="number"
          min="1"
          max="65535"
          [value]="port()"
          (input)="port.set(+$any($event.target).value)"
        />
      </div>

      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="reg-user" class="text-muted-foreground text-xs uppercase tracking-wide">Username</label>
        <input
          hlmInput
          id="reg-user"
          placeholder="postgres"
          [value]="username()"
          (input)="username.set($any($event.target).value)"
        />
      </div>

      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="reg-password" class="text-muted-foreground text-xs uppercase tracking-wide">Password</label>
        <input
          hlmInput
          id="reg-password"
          type="password"
          autocomplete="off"
          [value]="password()"
          (input)="password.set($any($event.target).value)"
        />
      </div>

      <div class="col-span-2 flex flex-col gap-1.5">
        <label hlmLabel for="reg-db" class="text-muted-foreground text-xs uppercase tracking-wide">Default database</label>
        <input
          hlmInput
          id="reg-db"
          placeholder="postgres"
          [value]="databaseName()"
          (input)="databaseName.set($any($event.target).value)"
        />
      </div>
    </div>

    @if (error(); as err) {
      <p class="text-destructive text-sm">{{ err }}</p>
    }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" (click)="close()" [disabled]="submitting()">Cancel</button>
      <button hlmBtn type="button" (click)="submit()" [disabled]="!canSubmit()">
        {{ submitting() ? 'Testing connection…' : 'Register' }}
      </button>
    </div>
  `,
})
export class DatabaseRegisterExternalDialog {
  protected readonly store = inject(DatabasesStore);
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  private readonly api = inject(DatabaseService);

  protected readonly engine = signal<string | null>(null);
  protected readonly version = signal('');
  protected readonly displayName = signal('');
  protected readonly host = signal('');
  protected readonly port = signal<number>(5432);
  protected readonly username = signal('');
  protected readonly password = signal('');
  protected readonly databaseName = signal('');
  // Set when the user picks a discover candidate. Passing both upstream marks the row as
  // "adopted Docker container" - KRINT enables upgrade/backup/exec against it even though
  // IsManaged stays false.
  protected readonly adoptedContainerId = signal<string | null>(null);
  protected readonly adoptedContainerName = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  // Discover state. `null` means "not scanned yet" so the empty-state prompt is shown
  // instead of "no candidates found".
  protected readonly candidates = signal<ReadonlyArray<DiscoveredContainerDto> | null>(null);
  protected readonly scanning = signal(false);
  protected readonly scanError = signal<string | null>(null);

  protected readonly canSubmit = computed(() =>
    !this.submitting() &&
    !!this.engine() &&
    this.version().trim() !== '' &&
    this.displayName().trim() !== '' &&
    this.host().trim() !== '' &&
    this.port() > 0 && this.port() <= 65535 &&
    this.username().trim() !== '' &&
    this.password() !== '' &&
    this.databaseName().trim() !== '',
  );

  protected scan(): void {
    this.scanning.set(true);
    this.scanError.set(null);
    this.api.apiDatabaseDiscoverGet().subscribe({
      next: (list) => {
        this.candidates.set(list);
        this.scanning.set(false);
      },
      error: (err: unknown) => {
        this.scanError.set(messageOf(err));
        this.scanning.set(false);
      },
    });
  }

  protected pickCandidate(c: DiscoveredContainerDto): void {
    this.engine.set(c.engine);
    this.version.set(c.version);
    this.displayName.set(c.containerName);
    this.host.set(c.host);
    // c.port is typed as DashboardStatsDtoTotalInstances (an empty interface the generator
    // emits for int32). It's a number at runtime - cast to use it as one.
    const portNum = c.port as unknown as number;
    if (portNum > 0) this.port.set(portNum);
    this.username.set(c.username);
    if (c.password) this.password.set(c.password);
    this.databaseName.set(c.databaseName);
    this.adoptedContainerId.set(c.containerId);
    this.adoptedContainerName.set(c.containerName);
  }

  protected submit(): void {
    if (!this.canSubmit()) return;
    this.submitting.set(true);
    this.error.set(null);
    this.store.registerExternal({
      dto: {
        engine: this.engine()!,
        version: this.version().trim(),
        displayName: this.displayName().trim(),
        host: this.host().trim(),
        port: this.port() as unknown as never, // openapi-gen wraps int32 in an empty interface
        username: this.username().trim(),
        password: this.password(),
        databaseName: this.databaseName().trim(),
        containerId: this.adoptedContainerId(),
        containerName: this.adoptedContainerName(),
      },
      onResult: (res) => {
        this.submitting.set(false);
        if ('error' in res) {
          this.error.set(res.error);
        } else {
          this.ref.close();
        }
      },
    });
  }

  protected close(): void {
    this.ref.close();
  }
}

function messageOf(err: unknown): string {
  if (err && typeof err === 'object' && 'error' in err) {
    const e = (err as { error: unknown }).error;
    if (e && typeof e === 'object' && 'error' in e) return String((e as { error: unknown }).error);
  }
  if (err instanceof Error) return err.message;
  return 'Scan failed';
}

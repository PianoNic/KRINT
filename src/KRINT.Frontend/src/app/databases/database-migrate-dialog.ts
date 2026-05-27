import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowRight, lucideCheck, lucideClipboardCopy, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { Subscription } from 'rxjs';
import { DiscoveredContainerDto } from '../api/model/discoveredContainerDto';
import { MigrationProgressDto, CleanupStepDto } from '../api/model/migrationProgressDto';
import { MigrationRequestDto } from '../api/model/migrationRequestDto';
import { ProvisionedDatabaseDto } from '../api/model/provisionedDatabaseDto';
import { MigrationHubService } from './migration-hub.service';
import { DatabasesStore } from '../shared/stores/databases.store';

/**
 * Guided-migration wizard. Opens with a discovered candidate, then walks three phases:
 *   review -> running -> done|failed.
 * The hub stream is opened on "Start migration" and yields one progress event per server step.
 * On done, the new instance + cleanup steps are shown; on failed, the partial error is shown
 * and the user is reminded that the half-provisioned target instance was left running.
 *
 * Source credentials default to whatever discovery parsed off the source container's env. The
 * user can edit them - useful when POSTGRES_PASSWORD wasn't set via env (so discovery left it
 * blank) or when the container uses a non-default user.
 */
@Component({
  selector: 'app-database-migrate-dialog',
  imports: [
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmInputImports,
    HlmLabelImports,
  ],
  providers: [provideIcons({ lucideArrowRight, lucideCheck, lucideClipboardCopy, lucideTriangleAlert })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Migrate into KRINT</h3>
      <p hlmDialogDescription>
        Provision a fresh KRINT-managed instance and copy data from <strong>{{ candidate.containerName }}</strong> into it.
        The source container is left running - you remove it from your compose file once you've verified the new instance.
      </p>
    </hlm-dialog-header>

    @switch (phase()) {
      @case ('review') {
        <section class="grid grid-cols-2 gap-3">
          <div class="col-span-2 flex items-center gap-2 text-sm">
            <span hlmBadge variant="outline" class="text-[10px] uppercase">{{ candidate.engine }}</span>
            <span class="font-mono">{{ candidate.image }}</span>
            @if (candidate.composeProject) {
              <span hlmBadge variant="secondary" class="text-[10px] normal-case">compose: {{ candidate.composeProject }}</span>
            }
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="mig-name" class="text-muted-foreground text-xs uppercase tracking-wide">New instance name</label>
            <input hlmInput id="mig-name" [value]="targetDisplayName()" (input)="targetDisplayName.set($any($event.target).value)" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="mig-ver" class="text-muted-foreground text-xs uppercase tracking-wide">Target version</label>
            <input hlmInput id="mig-ver" [value]="targetVersion()" (input)="targetVersion.set($any($event.target).value)" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="mig-user" class="text-muted-foreground text-xs uppercase tracking-wide">Source user</label>
            <input hlmInput id="mig-user" [value]="sourceUsername()" (input)="sourceUsername.set($any($event.target).value)" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="mig-pw" class="text-muted-foreground text-xs uppercase tracking-wide">Source password</label>
            <input hlmInput id="mig-pw" type="password" autocomplete="off" [value]="sourcePassword()" (input)="sourcePassword.set($any($event.target).value)" />
          </div>

          <div class="col-span-2 flex flex-col gap-1.5">
            <label hlmLabel for="mig-db" class="text-muted-foreground text-xs uppercase tracking-wide">Source database</label>
            <input hlmInput id="mig-db" [value]="sourceDatabaseName()" (input)="sourceDatabaseName.set($any($event.target).value)" />
          </div>
        </section>

        <p class="text-muted-foreground text-xs">
          Tip: stop writes to the source database before starting. KRINT does not lock it, so concurrent writes during the dump may be lost.
        </p>

        <div class="flex justify-end gap-2">
          <button hlmBtn variant="outline" type="button" (click)="close()">Cancel</button>
          <button hlmBtn type="button" (click)="start()" [disabled]="!canStart()">Start migration</button>
        </div>
      }

      @case ('running') {
        <section class="flex flex-col gap-3">
          <p class="text-sm">Migrating <strong>{{ candidate.containerName }}</strong> -&gt; <strong>{{ targetDisplayName() }}</strong></p>

          <ol class="flex flex-col gap-1.5">
            @for (step of steps; track step.slug; let i = $index) {
              <li class="flex items-center gap-2 text-sm"
                  [class.text-muted-foreground]="i + 1 > currentStep()"
                  [class.font-medium]="i + 1 === currentStep()">
                @if (i + 1 < currentStep()) {
                  <ng-icon name="lucideCheck" size="14" class="text-emerald-600" />
                } @else if (i + 1 === currentStep()) {
                  <span class="inline-block h-2 w-2 animate-pulse rounded-full bg-primary"></span>
                } @else {
                  <span class="inline-block h-2 w-2 rounded-full bg-muted"></span>
                }
                <span>{{ step.label }}</span>
              </li>
            }
          </ol>

          @if (latestMessage(); as msg) {
            <p class="text-muted-foreground font-mono text-xs">{{ msg }}</p>
          }
        </section>

        <div class="flex justify-end">
          <button hlmBtn variant="outline" type="button" (click)="close()">Run in background</button>
        </div>
      }

      @case ('done') {
        <section class="flex flex-col gap-4">
          <div class="flex items-center gap-2 text-sm font-medium text-emerald-700">
            <ng-icon name="lucideCheck" size="16" />
            Migration complete.
          </div>

          @if (result(); as r) {
            <div class="rounded-md border border-border p-3 text-xs flex flex-col gap-1">
              <div><span class="text-muted-foreground">New instance:</span> {{ r.containerName }}</div>
              <div class="flex items-center gap-2">
                <span class="text-muted-foreground">Connection string:</span>
                <code class="font-mono break-all">{{ r.connectionString }}</code>
                <button hlmBtn size="sm" variant="ghost" type="button" (click)="copy(r.connectionString)" aria-label="Copy connection string">
                  <ng-icon name="lucideClipboardCopy" size="12" />
                </button>
              </div>
            </div>
          }

          <div class="flex flex-col gap-2">
            <h4 class="text-sm font-medium">Cleanup checklist</h4>
            <ol class="flex flex-col gap-2">
              @for (c of cleanup(); track $index) {
                <li class="rounded-md border border-border p-2 text-sm">
                  <div class="font-medium">{{ c.title }}</div>
                  <div class="text-muted-foreground text-xs mt-0.5 whitespace-pre-wrap">{{ c.detail }}</div>
                </li>
              }
            </ol>
          </div>
        </section>

        <div class="flex justify-end">
          <button hlmBtn type="button" (click)="close()">Done</button>
        </div>
      }

      @case ('failed') {
        <section class="flex flex-col gap-3">
          <div class="flex items-center gap-2 text-sm font-medium text-destructive">
            <ng-icon name="lucideTriangleAlert" size="16" />
            Migration failed.
          </div>
          <p class="text-sm">{{ latestMessage() || 'The server reported an error.' }}</p>
        </section>

        <div class="flex justify-end gap-2">
          <button hlmBtn variant="outline" type="button" (click)="back()">Back to review</button>
          <button hlmBtn type="button" (click)="close()">Close</button>
        </div>
      }
    }
  `,
})
export class DatabaseMigrateDialog {
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  private readonly hub = inject(MigrationHubService);
  private readonly store = inject(DatabasesStore);
  protected readonly candidate = injectBrnDialogContext<DiscoveredContainerDto>();

  protected readonly phase = signal<'review' | 'running' | 'done' | 'failed'>('review');
  protected readonly targetDisplayName = signal(this.defaultTargetName(this.candidate));
  protected readonly targetVersion = signal(this.candidate.version || 'latest');
  protected readonly sourceUsername = signal(this.candidate.username);
  protected readonly sourcePassword = signal(this.candidate.password ?? '');
  protected readonly sourceDatabaseName = signal(this.candidate.databaseName);

  protected readonly currentStep = signal(0);
  protected readonly latestMessage = signal<string | null>(null);
  protected readonly result = signal<ProvisionedDatabaseDto | null>(null);
  protected readonly cleanup = signal<ReadonlyArray<CleanupStepDto>>([]);

  // Matches the 5 phases yielded by StreamMigrateContainerCommandHandler. Order matches the
  // CurrentStep values the server emits, so a step is "in progress" when its index + 1 equals
  // currentStep() and "complete" when it's less.
  protected readonly steps = [
    { slug: 'probe-source',    label: 'Probe source database' },
    { slug: 'provision-target', label: 'Provision target instance' },
    { slug: 'dump-source',     label: 'Dump source data' },
    { slug: 'restore-target',  label: 'Restore into target' },
    { slug: 'done',            label: 'Finalise' },
  ];

  private subscription: Subscription | null = null;

  protected readonly canStart = computed(() =>
    this.targetDisplayName().trim() !== '' &&
    this.targetVersion().trim() !== '' &&
    this.sourceUsername().trim() !== '' &&
    this.sourcePassword() !== '' &&
    this.sourceDatabaseName().trim() !== '',
  );

  protected start(): void {
    if (!this.canStart()) return;
    this.phase.set('running');
    this.currentStep.set(1);
    this.latestMessage.set(null);

    const portNum = this.candidate.port as unknown as number;
    const request: MigrationRequestDto = {
      sourceContainerId: this.candidate.containerId,
      sourceHost: this.candidate.host,
      sourcePort: portNum as unknown as never,
      sourceUsername: this.sourceUsername().trim(),
      sourcePassword: this.sourcePassword(),
      sourceDatabaseName: this.sourceDatabaseName().trim(),
      sourceEngine: this.candidate.engine,
      targetEngine: this.candidate.engine,
      targetVersion: this.targetVersion().trim(),
      targetDisplayName: this.targetDisplayName().trim(),
      composeProject: this.candidate.composeProject ?? null,
      composeService: this.candidate.composeService ?? null,
      composeFilePath: this.candidate.composeFilePath ?? null,
    };

    this.subscription = this.hub.stream(request).subscribe({
      next: (ev) => this.applyProgress(ev),
      error: (err: unknown) => {
        this.latestMessage.set(messageOf(err));
        this.phase.set('failed');
      },
      complete: () => {
        // Hub completes naturally after the terminal event we already handled. Refresh the
        // databases list so the new instance shows up immediately.
        this.store.load();
      },
    });
  }

  protected back(): void {
    this.phase.set('review');
    this.currentStep.set(0);
    this.latestMessage.set(null);
  }

  protected copy(text: string): void {
    navigator.clipboard?.writeText(text);
  }

  protected close(): void {
    try { this.subscription?.unsubscribe(); } catch { /* already gone */ }
    this.ref.close();
  }

  private applyProgress(ev: MigrationProgressDto): void {
    this.latestMessage.set(ev.message);
    const step = ev.currentStep as unknown as number;
    if (typeof step === 'number' && step > 0) this.currentStep.set(step);

    if (ev.status === 'done') {
      if (ev.result) this.result.set(ev.result);
      if (ev.cleanup) this.cleanup.set(ev.cleanup);
      this.phase.set('done');
    } else if (ev.status === 'failed') {
      this.phase.set('failed');
    }
  }

  private defaultTargetName(c: DiscoveredContainerDto): string {
    return c.composeService ? `${c.composeService}-krint` : `${c.containerName}-krint`;
  }
}

function messageOf(err: unknown): string {
  if (err instanceof Error) return err.message;
  if (typeof err === 'string') return err;
  return 'Migration failed';
}

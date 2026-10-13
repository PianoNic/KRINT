import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucideCheck,
  lucideChevronDown,
  lucideHourglass,
  lucideRotateCw,
  lucideTriangleAlert,
  lucideX,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSpinnerImports } from '@spartan-ng/helm/spinner';
import {
  ProvisionEvent,
  ProvisionPlan,
  ProvisionStep,
  buildSteps,
  formatElapsed,
  friendlyFailure,
  isShortError,
  slowHint,
} from './provision-steps';

const CONTAINER_STEPS: ReadonlySet<string> = new Set(['start', 'wait', 'defaultDb', 'plugins']);

/**
 * The live view of a running provision: a vertical timeline of friendly steps fed by the
 * provision hub's raw progress lines, plus the failure state with Retry / Back.
 */
@Component({
  selector: 'app-provision-progress',
  imports: [NgIcon, HlmButtonImports, HlmCardImports, HlmSpinnerImports],
  providers: [
    provideIcons({
      lucideArrowLeft,
      lucideCheck,
      lucideChevronDown,
      lucideHourglass,
      lucideRotateCw,
      lucideTriangleAlert,
      lucideX,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section hlmCard aria-labelledby="provision-progress-title">
      <div hlmCardHeader class="flex flex-row items-center gap-4">
        <span
          class="bg-muted text-foreground relative flex size-12 shrink-0 items-center justify-center rounded-xl"
          aria-hidden="true"
        >
          <ng-icon [name]="engineIcon()" size="24" />
          <span
            class="bg-background absolute -right-1.5 -bottom-1.5 flex size-6 items-center justify-center rounded-full border"
          >
            @if (failed()) {
              <ng-icon name="lucideX" size="12" class="text-destructive" />
            } @else {
              <hlm-spinner aria-hidden="true" class="text-xs" />
            }
          </span>
        </span>
        <div class="flex min-w-0 flex-1 flex-col gap-1">
          <h2
            #heading
            id="provision-progress-title"
            hlmCardTitle
            tabindex="-1"
            class="text-lg leading-tight outline-none"
          >
            @if (failed()) {
              We couldn't launch your {{ engineName() }} database
            } @else {
              Launching your {{ engineName() }} database
            }
          </h2>
          <p hlmCardDescription>
            <span class="text-foreground font-medium">{{ displayName() }}</span>
            @if (!failed()) {
              <span> - step {{ stepNumber() }} of {{ steps().length }}</span>
            }
          </p>
        </div>
        @if (!failed()) {
          <span
            class="text-muted-foreground shrink-0 rounded-full border px-2.5 py-1 font-mono text-xs tabular-nums"
            role="timer"
            [attr.aria-label]="'Elapsed time ' + totalElapsed()"
          >
            {{ totalElapsed() }}
          </span>
        }
      </div>

      <div hlmCardContent class="flex flex-col gap-5">
        @if (failed()) {
          <div
            #failure
            tabindex="-1"
            role="alert"
            class="border-destructive/40 bg-destructive/5 flex flex-col gap-3 rounded-lg border p-4 outline-none"
          >
            <div class="flex items-start gap-3">
              <ng-icon name="lucideTriangleAlert" size="18" class="text-destructive mt-0.5 shrink-0" />
              <div class="flex flex-col gap-1">
                <p class="text-sm font-medium">{{ failureSummary() }}</p>
                @if (shortError()) {
                  <p class="text-sm">{{ error() }}</p>
                }
                @if (containerStarted()) {
                  <p class="text-muted-foreground text-sm">
                    The partly created container was removed, so it is safe to try again.
                  </p>
                }
              </div>
            </div>
            @if (!shortError()) {
              <details class="group bg-background/60 rounded-md border">
                <summary
                  class="text-muted-foreground hover:text-foreground flex cursor-pointer list-none items-center gap-1.5 px-3 py-2 text-xs font-medium [&::-webkit-details-marker]:hidden"
                >
                  <ng-icon
                    name="lucideChevronDown"
                    size="14"
                    class="motion-safe:transition-transform -rotate-90 group-open:rotate-0"
                  />
                  Show error details
                </summary>
                <pre
                  class="text-foreground max-h-64 overflow-auto border-t px-3 py-2 font-mono text-xs break-words whitespace-pre-wrap"
                >{{ error() }}</pre>
              </details>
            }
          </div>
        }

        <ol class="flex flex-col" aria-label="Launch steps">
          @for (s of steps(); track s.id; let last = $last) {
            <li class="relative flex gap-3" [class.pb-5]="!last" [attr.aria-current]="s.state === 'active' ? 'step' : null">
              @if (!last) {
                <span
                  aria-hidden="true"
                  class="absolute top-8 bottom-0 left-[15px] w-0.5 rounded-full motion-safe:transition-colors"
                  [class.bg-emerald-600]="s.state === 'done'"
                  [class.bg-border]="s.state !== 'done'"
                ></span>
              }
              <span
                aria-hidden="true"
                class="relative flex size-8 shrink-0 items-center justify-center rounded-full border-2 motion-safe:transition-colors"
                [class]="markerClass(s)"
              >
                @switch (s.state) {
                  @case ('done') {
                    <ng-icon name="lucideCheck" size="16" />
                  }
                  @case ('active') {
                    <hlm-spinner aria-hidden="true" />
                  }
                  @case ('failed') {
                    <ng-icon name="lucideX" size="16" />
                  }
                  @default {
                    <span class="bg-muted-foreground/50 size-1.5 rounded-full"></span>
                  }
                }
              </span>
              <div class="flex min-w-0 flex-1 flex-col gap-0.5 pt-1">
                <div class="flex items-baseline justify-between gap-3">
                  <span
                    class="text-sm"
                    [class.font-medium]="s.state === 'active' || s.state === 'failed'"
                    [class.text-muted-foreground]="s.state === 'pending'"
                    [class.text-destructive]="s.state === 'failed'"
                  >
                    <span class="sr-only">{{ stateLabel(s) }}: </span>{{ s.title }}
                  </span>
                  @if (durationOf(s); as d) {
                    <span
                      class="text-muted-foreground shrink-0 font-mono text-xs tabular-nums"
                      [attr.aria-label]="(s.state === 'active' ? 'running for ' : 'took ') + d"
                      >{{ d }}</span
                    >
                  }
                </div>
                @if (s.detail) {
                  <span class="text-muted-foreground font-mono text-xs break-all">{{ s.detail }}</span>
                }
                @for (w of s.warnings; track $index) {
                  <span class="flex items-start gap-1.5 text-xs text-amber-700 dark:text-amber-400">
                    <ng-icon name="lucideTriangleAlert" size="12" class="mt-0.5 shrink-0" />
                    <span>{{ w }}</span>
                  </span>
                }
                @if (s.state === 'active' && hint(); as h) {
                  <p
                    class="bg-muted/60 text-muted-foreground mt-2 flex items-start gap-2 rounded-md px-3 py-2 text-xs leading-relaxed"
                  >
                    <ng-icon name="lucideHourglass" size="14" class="text-foreground mt-px shrink-0" />
                    <span>{{ h }}</span>
                  </p>
                }
              </div>
            </li>
          }
        </ol>

        <p class="sr-only" role="status" aria-live="polite" aria-atomic="true">{{ announcement() }}</p>
      </div>

      <div hlmCardFooter class="justify-between gap-3 border-t pt-4">
        @if (failed()) {
          <button hlmBtn variant="outline" type="button" (click)="back.emit()">
            <ng-icon name="lucideArrowLeft" size="16" />
            Back to review
          </button>
          <button hlmBtn type="button" (click)="retry.emit()">
            <ng-icon name="lucideRotateCw" size="16" />
            Try again
          </button>
        } @else {
          <p class="text-muted-foreground text-xs">
            Keep this page open. It updates on its own and shows your connection details when it is done.
          </p>
          <span
            class="bg-primary text-primary-foreground inline-flex h-9 shrink-0 items-center gap-2 rounded-md px-4 text-sm font-medium"
            aria-hidden="true"
          >
            <hlm-spinner aria-hidden="true" />
            Launching
          </span>
        }
      </div>
    </section>
  `,
})
export class ProvisionProgress {
  private readonly injector = inject(Injector);

  readonly engineKey = input.required<string>();
  readonly engineName = input.required<string>();
  readonly engineIcon = input('lucideDatabase');
  readonly displayName = input('');
  readonly plan = input<ProvisionPlan>({ plugins: 0, extraDatabases: 0, users: 0 });
  readonly events = input.required<ReadonlyArray<ProvisionEvent>>();
  readonly startedAt = input.required<number>();
  readonly error = input<string | null>(null);

  readonly retry = output<void>();
  readonly back = output<void>();

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly failure = viewChild<ElementRef<HTMLElement>>('failure');

  // Ticks once a second while the provision runs so the elapsed counters stay live.
  private readonly now = signal(Date.now());

  protected readonly failed = computed(() => this.error() !== null);

  // After a failure the steps that never ran are noise; only what happened stays listed.
  protected readonly steps = computed(() => {
    const all = buildSteps(this.events(), this.engineName(), this.plan(), this.failed() ? 'failed' : 'running');
    return this.failed() ? all.filter((s) => s.state !== 'pending') : all;
  });

  protected readonly current = computed(
    () => this.steps().find((s) => s.state === 'active' || s.state === 'failed') ?? null,
  );

  protected readonly stepNumber = computed(() => {
    const cur = this.current();
    return cur ? this.steps().indexOf(cur) + 1 : 1;
  });

  protected readonly totalElapsed = computed(() => formatElapsed(this.now() - this.startedAt()));

  protected readonly hint = computed(() => slowHint(this.current(), this.engineKey(), this.now()));

  protected readonly failureSummary = computed(() => friendlyFailure(this.error() ?? ''));
  protected readonly shortError = computed(() => isShortError(this.error() ?? ''));
  // Only after the container was started is there anything to clean up worth mentioning.
  protected readonly containerStarted = computed(() =>
    this.steps().some((s) => s.state !== 'pending' && CONTAINER_STEPS.has(s.key)),
  );

  protected readonly announcement = computed(() => {
    if (this.failed()) return `Launch failed. ${this.failureSummary()}`;
    const cur = this.current();
    return cur ? `${cur.title}, step ${this.stepNumber()} of ${this.steps().length}` : '';
  });

  constructor() {
    effect((onCleanup) => {
      if (this.failed()) return;
      const id = setInterval(() => this.now.set(Date.now()), 1000);
      onCleanup(() => clearInterval(id));
    });

    // The Launch button the user just pressed is gone; put focus on what replaced it, and move it
    // to the explanation when the provision fails.
    afterNextRender(() => this.heading()?.nativeElement.focus());
    effect(() => {
      if (!this.failed()) return;
      afterNextRender(() => this.failure()?.nativeElement.focus(), { injector: this.injector });
    });
  }

  protected markerClass(s: ProvisionStep): string {
    switch (s.state) {
      case 'done':
        return 'border-emerald-600 bg-emerald-600 text-white';
      case 'active':
        return 'border-primary bg-background text-foreground ring-4 ring-primary/10';
      case 'failed':
        return 'border-destructive bg-background text-destructive';
      default:
        return 'border-border bg-background';
    }
  }

  protected stateLabel(s: ProvisionStep): string {
    switch (s.state) {
      case 'done':
        return 'Done';
      case 'active':
        return 'In progress';
      case 'failed':
        return 'Failed';
      default:
        return 'Up next';
    }
  }

  protected durationOf(s: ProvisionStep): string | null {
    if (s.startedAt === null) return null;
    if (s.state === 'active') return formatElapsed(this.now() - s.startedAt);
    if (s.endedAt === null) return null;
    const ms = s.endedAt - s.startedAt;
    return ms < 1000 ? null : formatElapsed(ms);
  }
}

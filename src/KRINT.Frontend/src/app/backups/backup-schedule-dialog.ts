import { ChangeDetectionStrategy, Component, computed, inject, Injectable, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogService, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { DatabaseInstanceDto } from '../api/model/databaseInstanceDto';
import { BackupsService } from '../api/api/backups.service';
import { BackupScheduleDto } from '../api/model/backupScheduleDto';

export type ScheduleDialogContext = {
  instances: ReadonlyArray<DatabaseInstanceDto>;
  preselectedInstanceId: string | null;
};

type Template = {
  key: string;
  label: string;
  cron: string;
  description: string;
  helpText: string;
};

export const SCHEDULE_TEMPLATES: ReadonlyArray<Template> = [
  { key: 'hourly',  label: 'Every hour',                cron: '0 * * * *',  description: 'Hourly backup',                helpText: 'Runs at minute 0 of every hour - e.g. 13:00, 14:00, 15:00…' },
  { key: '6hours', label: 'Every 6 hours',              cron: '0 */6 * * *', description: '6-hourly backup',              helpText: 'Runs at 00:00, 06:00, 12:00 and 18:00 every day.' },
  { key: 'daily',  label: 'Every day at 03:00',         cron: '0 3 * * *',  description: 'Daily 03:00 backup',           helpText: 'Runs once a day at 3 AM - picked because most systems are quiet at that hour.' },
  { key: 'noon',   label: 'Every day at 12:00',         cron: '0 12 * * *', description: 'Daily noon backup',            helpText: 'Runs once a day at noon.' },
  { key: 'weekly', label: 'Every Monday at 03:00',      cron: '0 3 * * 1',  description: 'Weekly Monday 03:00 backup',   helpText: 'Runs once a week, Monday 3 AM.' },
  { key: 'monthly',label: 'First day of month at 03:00',cron: '0 3 1 * *',  description: 'Monthly 1st 03:00 backup',     helpText: 'Runs on the 1st of every month at 3 AM.' },
];

@Component({
  selector: 'app-backup-schedule-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription, HlmInputImports, HlmLabelImports, HlmSelectImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Schedule a backup</h3>
      <p hlmDialogDescription>
        Pick when KRINT should snapshot an instance for you. Choose a preset, or enter a cron expression directly if you know what you're doing.
      </p>
    </hlm-dialog-header>

    <div class="flex flex-col gap-2">
      <label hlmLabel for="schedule-instance">Instance</label>
      <hlm-select id="schedule-instance" [value]="instanceId()" (valueChange)="instanceId.set($event)">
        <hlm-select-trigger class="w-full">
          <hlm-select-value placeholder="Select an instance" />
        </hlm-select-trigger>
        <hlm-select-content *hlmSelectPortal>
          @for (i of ctx.instances; track i.id) {
            <hlm-select-item [value]="i.id">{{ i.engine }} - {{ i.containerName }}</hlm-select-item>
          }
        </hlm-select-content>
      </hlm-select>
    </div>

    <div class="flex flex-col gap-2">
      <label hlmLabel>Preset</label>
      <div class="grid grid-cols-1 gap-2 sm:grid-cols-2">
        @for (t of templates; track t.key) {
          <button
            type="button"
            class="hover:bg-muted/60 group flex flex-col items-start gap-1 rounded-md border p-3 text-left transition-colors"
            [class.border-primary]="selectedTemplate() === t.key"
            [class.bg-muted]="selectedTemplate() === t.key"
            (click)="pickTemplate(t)"
          >
            <span class="text-sm font-medium">{{ t.label }}</span>
            <span class="text-muted-foreground text-xs">{{ t.helpText }}</span>
          </button>
        }
        <button
          type="button"
          class="hover:bg-muted/60 flex flex-col items-start gap-1 rounded-md border border-dashed p-3 text-left transition-colors sm:col-span-2"
          [class.border-primary]="selectedTemplate() === 'custom'"
          [class.bg-muted]="selectedTemplate() === 'custom'"
          (click)="pickCustom()"
        >
          <span class="text-sm font-medium">Advanced - custom cron expression</span>
          <span class="text-muted-foreground text-xs">5 fields: minute / hour / day-of-month / month / day-of-week. Times are UTC.</span>
        </button>
      </div>
    </div>

    @if (selectedTemplate() === 'custom') {
      <div class="flex flex-col gap-2">
        <label hlmLabel for="cron-input">Cron expression</label>
        <input hlmInput id="cron-input" placeholder="0 3 * * *" [value]="cron()" (input)="cron.set($any($event.target).value)" class="font-mono" />
        <p class="text-muted-foreground text-xs">
          Examples: <code class="font-mono">0 3 * * *</code> (daily 03:00),
          <code class="font-mono">*/15 * * * *</code> (every 15 min),
          <code class="font-mono">0 3 * * 1-5</code> (weekdays 03:00).
        </p>
      </div>
    }

    @if (error(); as err) {
      <p class="text-destructive text-sm">{{ err }}</p>
    }

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" (click)="cancel()" [disabled]="saving()">Cancel</button>
      <button hlmBtn type="button" [disabled]="!canSave() || saving()" (click)="save()">
        {{ saving() ? 'Saving…' : 'Create schedule' }}
      </button>
    </div>
  `,
})
export class BackupScheduleDialog {
  private readonly ref = inject(BrnDialogRef);
  private readonly api = inject(BackupsService);
  protected readonly ctx = injectBrnDialogContext<ScheduleDialogContext>();

  protected readonly templates = SCHEDULE_TEMPLATES;
  protected readonly instanceId = signal<string | null>(null);
  protected readonly selectedTemplate = signal<string | null>(null);
  protected readonly cron = signal('');
  protected readonly description = signal('');
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly canSave = computed(() => {
    if (!this.instanceId() || !this.selectedTemplate()) return false;
    return this.cron().trim().split(/\s+/).length === 5;
  });

  constructor() {
    this.instanceId.set(this.ctx.preselectedInstanceId);
  }

  protected pickTemplate(t: Template): void {
    this.selectedTemplate.set(t.key);
    this.cron.set(t.cron);
    this.description.set(t.description);
  }

  protected pickCustom(): void {
    this.selectedTemplate.set('custom');
    this.description.set('Custom schedule');
  }

  protected save(): void {
    if (!this.canSave()) return;
    this.saving.set(true);
    this.error.set(null);
    this.api.apiBackupsSchedulesPost({
      instanceId: this.instanceId()!,
      cronExpression: this.cron().trim(),
      description: this.description() || this.cron().trim(),
    }).subscribe({
      next: (created) => this.ref.close(created),
      error: (err) => {
        this.error.set(messageOf(err) ?? 'Failed to create schedule');
        this.saving.set(false);
      },
    });
  }

  protected cancel(): void { this.ref.close(null); }
}

@Injectable({ providedIn: 'root' })
export class BackupScheduleDialogService {
  private readonly dialog = inject(HlmDialogService);

  open(ctx: ScheduleDialogContext): Promise<BackupScheduleDto | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(BackupScheduleDialog, {
        context: ctx,
        contentClass: 'sm:max-w-2xl',
      });
      ref.closed$.subscribe((result) => resolve((result as BackupScheduleDto | null) ?? null));
    });
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

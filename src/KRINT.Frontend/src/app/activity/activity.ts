import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { ContentHeader } from '../shared/components/content-header/content-header';
import { ActivityService } from '../api/api/activity.service';
import { ActivityEntryDto } from '../api/model/activityEntryDto';

@Component({
  selector: 'app-activity',
  imports: [
    ContentHeader,
    DatePipe,
    NgIcon,
    HlmBadgeImports,
    HlmButtonImports,
    HlmCardImports,
    HlmTableImports,
  ],
  providers: [provideIcons({ lucideRefreshCw })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />
    <section class="flex flex-col gap-4 px-4 py-6">
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>Activity</h2>
          <p hlmCardDescription>Provisioning, deletion, and user-management events.</p>
          <div hlmCardAction>
            <button hlmBtn variant="outline" size="sm" type="button" (click)="reload()" [disabled]="loading()">
              <ng-icon name="lucideRefreshCw" size="14" />
              {{ loading() ? 'Loading…' : 'Refresh' }}
            </button>
          </div>
        </div>

        <div hlmCardContent>
          @if (entries().length === 0 && !loading()) {
            <p class="text-muted-foreground text-sm">No activity yet.</p>
          } @else {
            <div hlmTableContainer>
              <table hlmTable>
                <thead hlmTableHeader>
                  <tr hlmTableRow>
                    <th hlmTableHead>When</th>
                    <th hlmTableHead>Action</th>
                    <th hlmTableHead>Engine</th>
                    <th hlmTableHead>Target</th>
                    <th hlmTableHead>Details</th>
                  </tr>
                </thead>
                <tbody hlmTableBody>
                  @for (e of entries(); track e.id) {
                    <tr hlmTableRow>
                      <td hlmTableCell class="font-mono text-xs">{{ e.createdAt | date: 'yyyy-MM-dd HH:mm:ss' }}</td>
                      <td hlmTableCell>
                        <span hlmBadge variant="secondary" class="font-mono text-xs">{{ e.action }}</span>
                      </td>
                      <td hlmTableCell class="text-sm">{{ e.engine ?? ' - ' }}</td>
                      <td hlmTableCell class="font-mono text-xs">{{ e.target }}</td>
                      <td hlmTableCell class="text-muted-foreground text-xs">{{ e.details ?? '' }}</td>
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
    </section>
  `,
})
export class Activity {
  private readonly api = inject(ActivityService);

  protected readonly entries = signal<ReadonlyArray<ActivityEntryDto>>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.reload();
  }

  protected reload(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.apiActivityGet().subscribe({
      next: (entries) => {
        this.entries.set(entries);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err instanceof Error ? err.message : 'Failed to load activity');
        this.loading.set(false);
      },
    });
  }
}

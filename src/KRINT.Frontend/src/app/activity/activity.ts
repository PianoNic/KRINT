import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
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
    HlmInputImports,
    HlmTableImports,
  ],
  providers: [provideIcons({ lucideRefreshCw, lucideSearch })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-content-header />

    <!-- Bare table layout: the toolbar pins to the top, the table beneath it claims the
         remaining space and scrolls its own body. -->
    <section class="flex flex-1 min-h-0 flex-col border-t">
      <header class="flex items-center justify-between gap-2 border-b px-4 py-2">
        <h2 class="text-sm font-medium">Activity</h2>
        <div class="flex items-center gap-2">
          <div class="relative">
            <ng-icon name="lucideSearch" size="14" class="text-muted-foreground absolute left-2 top-1/2 -translate-y-1/2" />
            <input
              hlmInput
              placeholder="Filter…"
              class="h-8 w-56 pl-7 text-xs"
              [value]="filter()"
              (input)="filter.set($any($event.target).value)"
            />
          </div>
          <button hlmBtn variant="outline" size="sm" type="button" (click)="reload()" [disabled]="loading()">
            <ng-icon name="lucideRefreshCw" size="14" />
            {{ loading() ? 'Loading…' : 'Refresh' }}
          </button>
        </div>
      </header>

      <div class="min-h-0 flex-1 overflow-auto">
        @if (filteredEntries().length === 0 && !loading()) {
          <p class="text-muted-foreground p-4 text-sm">
            @if (entries().length === 0) { No activity yet. } @else { No rows match "{{ filter() }}". }
          </p>
        } @else {
          <table hlmTable>
            <thead hlmTableHeader>
              <tr hlmTableRow>
                <th hlmTableHead>When</th>
                <th hlmTableHead>Who</th>
                <th hlmTableHead>Action</th>
                <th hlmTableHead>Engine</th>
                <th hlmTableHead>Target</th>
                <th hlmTableHead>Details</th>
              </tr>
            </thead>
            <tbody hlmTableBody>
              @for (e of filteredEntries(); track e.id) {
                <tr hlmTableRow>
                  <td hlmTableCell class="font-mono text-xs">{{ e.createdAt | date: 'yyyy-MM-dd HH:mm:ss' }}</td>
                  <td hlmTableCell class="text-sm">
                    @if (e.actorName) {
                      <span class="font-medium">{{ e.actorName }}</span>
                    } @else {
                      <span class="text-muted-foreground italic">system</span>
                    }
                  </td>
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
        }

        @if (error(); as err) {
          <p class="text-destructive mt-3 px-4 text-sm">{{ err }}</p>
        }
      </div>
    </section>
  `,
})
export class Activity {
  private readonly api = inject(ActivityService);

  protected readonly entries = signal<ReadonlyArray<ActivityEntryDto>>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly filter = signal('');

  // Substring match across every visible cell so a single search box covers action/engine/
  // target/details/actor in one go. Case-insensitive.
  protected readonly filteredEntries = computed(() => {
    const q = this.filter().trim().toLowerCase();
    if (q === '') return this.entries();
    return this.entries().filter((e) =>
      [e.action, e.engine, e.target, e.details, e.actorName]
        .some((v) => typeof v === 'string' && v.toLowerCase().includes(q)),
    );
  });

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

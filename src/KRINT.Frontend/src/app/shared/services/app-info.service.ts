import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { AppService } from '../../api/api/app.service';
import { AppDto } from '../../api/model/appDto';

/** How often the shell re-reads /api/app so the Docker banner clears once the daemon is back. */
const REFRESH_MS = 60_000;

/** What /api/app reports about the running backend: desktop shell or not, Docker reachable or not. */
@Injectable({ providedIn: 'root' })
export class AppInfoService {
  private readonly api = inject(AppService);
  private readonly info = signal<AppDto | null>(null);

  /** True inside the desktop shell, whose account password the shell itself owns. */
  readonly desktop = computed(() => this.info()?.desktop === true);
  /** Only true once the API has actually said Docker did not answer, never while loading. */
  readonly dockerUnreachable = computed(() => this.info()?.dockerReachable === false);

  constructor() {
    this.refresh();
    const timer = setInterval(() => this.refresh(), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  refresh(): void {
    this.api.apiAppGet().subscribe({
      next: (app) => this.info.set(app),
      error: () => undefined,
    });
  }
}

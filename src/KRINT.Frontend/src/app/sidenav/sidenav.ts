import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideActivity,
  lucideArchive,
  lucideChevronsUpDown,
  lucideDatabase,
  lucideLogOut,
  lucideMonitor,
  lucideMoon,
  lucidePlus,
  lucideServer,
  lucideSettings,
  lucideSun,
  lucideTable,
} from '@ng-icons/lucide';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { ThemeService, ThemeMode } from '../shared/services/theme.service';
import { AppService } from '../api/api/app.service';

@Component({
  selector: 'app-sidenav',
  imports: [
    HlmSidebarImports,
    HlmDropdownMenuImports,
    HlmAvatarImports,
    NgIcon,
    RouterLink,
  ],
  providers: [
    provideIcons({
      lucideActivity,
      lucideArchive,
      lucideChevronsUpDown,
      lucideDatabase,
      lucideLogOut,
      lucidePlus,
      lucideServer,
      lucideSettings,
      lucideSun,
      lucideMoon,
      lucideMonitor,
      lucideTable,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sidenav.html',
})
export class Sidenav {
  private readonly sidebarService = inject(HlmSidebarService);
  private readonly oidcSecurityService = inject(OidcSecurityService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly theme = inject(ThemeService);
  private readonly router = inject(Router);

  // Persist the sidebar open/closed state in localStorage so the user's choice survives
  // reloads. Spartan helm already cookies this, but the user explicitly wanted localStorage
  // so we mirror both ways: load it on construction, then push every change back.
  private static readonly STORAGE_KEY = 'krint.sidebar.open';

  constructor() {
    const saved = typeof localStorage !== 'undefined' ? localStorage.getItem(Sidenav.STORAGE_KEY) : null;
    if (saved !== null) {
      this.sidebarService.setOpen(saved === 'true');
    }
    effect(() => {
      const open = this.sidebarService.open();
      try { localStorage.setItem(Sidenav.STORAGE_KEY, String(open)); } catch { /* private mode */ }
    });
  }

  // Active-link signal driven by router events. The previous routerLinkActive="data-[active=true]"
  // was a no-op - routerLinkActive sets classes, not attributes, so no item ever lit up.
  // Here we derive the current URL once and let each menu item match against it.
  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );
  protected isRouteActive(route: string): boolean {
    const url = this.currentUrl();
    return url === route || url.startsWith(route + '/');
  }

  protected readonly themeMode = this.theme.mode;
  protected readonly navItems: ReadonlyArray<{ route: string; label: string; icon: string }> = [
    { route: '/instances', label: 'Instances', icon: 'lucideServer' },
    { route: '/browser',   label: 'Browser',   icon: 'lucideTable' },
    { route: '/backups',   label: 'Backups',   icon: 'lucideArchive' },
    { route: '/activity',  label: 'Activity',  icon: 'lucideActivity' },
    { route: '/settings',  label: 'Settings',  icon: 'lucideSettings' },
  ];

  protected readonly themeOptions: ReadonlyArray<{ mode: ThemeMode; label: string; icon: string }> = [
    { mode: 'light', label: 'Light', icon: 'lucideSun' },
    { mode: 'dark', label: 'Dark', icon: 'lucideMoon' },
    { mode: 'system', label: 'System', icon: 'lucideMonitor' },
  ];
  protected readonly menuSide = computed(() =>
    this.sidebarService.isMobile() ? 'top' : 'right',
  );

  private readonly appService = inject(AppService);
  protected readonly version = toSignal(
    this.appService.apiAppGet().pipe(map((app) => app.version ?? '')),
    { initialValue: '' },
  );

  private readonly userData = this.oidcSecurityService.userData;
  protected readonly user = computed(() => {
    const data = this.userData().userData;
    return {
      name: data?.preferred_username ?? data?.email ?? '',
      email: data?.email ?? '',
      avatar: data?.picture ?? '',
    };
  });

  protected setTheme(mode: ThemeMode): void {
    this.theme.set(mode);
  }

  protected logout(): void {
    this.oidcSecurityService
      .logoffAndRevokeTokens()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }
}

import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronsUpDown,
  lucideDatabase,
  lucideLogOut,
  lucideMonitor,
  lucideMoon,
  lucideSun,
} from '@ng-icons/lucide';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { ThemeService, ThemeMode } from '../shared/services/theme.service';

@Component({
  selector: 'app-sidenav',
  imports: [HlmSidebarImports, HlmDropdownMenuImports, HlmAvatarImports, NgIcon, RouterLink, RouterLinkActive],
  providers: [
    provideIcons({
      lucideChevronsUpDown,
      lucideDatabase,
      lucideLogOut,
      lucideSun,
      lucideMoon,
      lucideMonitor,
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

  protected readonly themeMode = this.theme.mode;
  protected readonly themeOptions: ReadonlyArray<{ mode: ThemeMode; label: string; icon: string }> = [
    { mode: 'light', label: 'Light', icon: 'lucideSun' },
    { mode: 'dark', label: 'Dark', icon: 'lucideMoon' },
    { mode: 'system', label: 'System', icon: 'lucideMonitor' },
  ];
  protected readonly menuSide = computed(() =>
    this.sidebarService.isMobile() ? 'top' : 'right',
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

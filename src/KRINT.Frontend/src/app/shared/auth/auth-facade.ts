import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { InjectionToken, Signal, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { OidcSecurityService, autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { Observable, catchError, firstValueFrom, from, switchMap, throwError } from 'rxjs';
import { environment } from '../environments/environment';

export type AuthMode = 'oidc' | 'local';

export type AuthUser = { name: string; email: string; avatar: string };

/**
 * What the rest of the app needs from authentication, whichever mode the backend runs in.
 * OIDC keeps angular-auth-oidc-client underneath; local mode talks to Toamaisutaa's password
 * endpoints served by the API itself. Components only ever see this.
 */
export interface AuthFacade {
  readonly mode: AuthMode;
  readonly user: Signal<AuthUser>;
  getAccessToken(): Promise<string>;
  logout(): void;
}

export const AUTH_FACADE = new InjectionToken<AuthFacade>('AUTH_FACADE');

/** Route guard that defers to whichever facade is installed. */
export const authGuard: CanActivateFn = (route, state) => {
  const facade = inject(AUTH_FACADE);
  if (facade.mode === 'oidc') return autoLoginPartialRoutesGuard(route, state);
  const local = facade as LocalAuthService;
  if (local.isSignedIn()) return true;
  return inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

// ---------------------------------------------------------------------------------------------
// OIDC
// ---------------------------------------------------------------------------------------------

export class OidcAuthFacade implements AuthFacade {
  readonly mode: AuthMode = 'oidc';
  private readonly oidc = inject(OidcSecurityService);
  private readonly userData = this.oidc.userData;

  readonly user = computed<AuthUser>(() => {
    const data = this.userData().userData;
    return {
      name: data?.preferred_username ?? data?.email ?? '',
      email: data?.email ?? '',
      avatar: data?.picture ?? '',
    };
  });

  async getAccessToken(): Promise<string> {
    return (await firstValueFrom(this.oidc.getAccessToken())) ?? '';
  }

  logout(): void {
    this.oidc.logoffAndRevokeTokens().subscribe();
  }
}

// ---------------------------------------------------------------------------------------------
// Local password login
// ---------------------------------------------------------------------------------------------

type TokenResponse = {
  access_token?: string;
  refresh_token?: string;
  expires_in?: number;
  two_factor_required?: boolean;
  error_description?: string;
};

const STORAGE_KEY = 'krint.local-session';

/**
 * Holds the token pair for local mode. The access token lives in memory and sessionStorage
 * (so a reload does not sign the user out); the refresh token sits in sessionStorage too,
 * which is the trade-off a same-origin SPA without a cookie-setting backend makes - it is
 * gone when the tab closes, and the Content-Security-Policy the API sends keeps script
 * injection out.
 */
export class LocalAuthService implements AuthFacade {
  readonly mode: AuthMode = 'local';
  private readonly router = inject(Router);
  private readonly accessToken = signal<string | null>(null);
  private readonly refreshToken = signal<string | null>(null);
  private refreshing: Promise<boolean> | null = null;

  readonly isSignedIn = computed(() => this.accessToken() !== null);

  readonly user = computed<AuthUser>(() => {
    const claims = decodeClaims(this.accessToken());
    return {
      name: claims['preferred_username'] ?? claims['name'] ?? claims['email'] ?? '',
      email: claims['email'] ?? '',
      avatar: claims['picture'] ?? '',
    };
  });

  constructor() {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (raw) {
        const saved = JSON.parse(raw) as { access?: string; refresh?: string };
        this.accessToken.set(saved.access ?? null);
        this.refreshToken.set(saved.refresh ?? null);
      }
    } catch {
      // storage unavailable or corrupt - start signed out
    }
  }

  async getAccessToken(): Promise<string> {
    return this.accessToken() ?? '';
  }

  /** Returns null on success, otherwise the message to show. */
  async signIn(identifier: string, password: string): Promise<string | null> {
    const response = await fetch(`${environment.apiBaseUrl}/auth/login`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ identifier, password }),
    });
    const body = (await response.json().catch(() => ({}))) as TokenResponse;
    if (response.status === 401) return body.error_description ?? 'The credentials are not valid.';
    if (!response.ok) return `Sign-in failed (${response.status}).`;
    if (body.two_factor_required) return 'This account requires a second factor, which the KRINT UI does not support yet.';
    if (!body.access_token) return 'The server returned no token.';
    this.store(body.access_token, body.refresh_token ?? null);
    return null;
  }

  /** Single-flight refresh: parallel 401s must not each spend the rotating refresh token. */
  refresh(): Promise<boolean> {
    this.refreshing ??= this.refreshOnce().finally(() => {
      this.refreshing = null;
    });
    return this.refreshing;
  }

  private async refreshOnce(): Promise<boolean> {
    const refreshToken = this.refreshToken();
    if (!refreshToken) return false;
    try {
      const response = await fetch(`${environment.apiBaseUrl}/auth/refresh`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });
      if (!response.ok) {
        this.clear();
        return false;
      }
      const body = (await response.json()) as TokenResponse;
      if (!body.access_token) {
        this.clear();
        return false;
      }
      this.store(body.access_token, body.refresh_token ?? refreshToken);
      return true;
    } catch {
      return false;
    }
  }

  logout(): void {
    const refreshToken = this.refreshToken();
    this.clear();
    if (refreshToken) {
      void fetch(`${environment.apiBaseUrl}/auth/logout`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      }).catch(() => undefined);
    }
    void this.router.navigate(['/login']);
  }

  private store(access: string, refresh: string | null): void {
    this.accessToken.set(access);
    this.refreshToken.set(refresh);
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify({ access, refresh }));
    } catch {
      // storage unavailable - the session lives for this page load only
    }
  }

  private clear(): void {
    this.accessToken.set(null);
    this.refreshToken.set(null);
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // ignore
    }
  }
}

/** Adds the bearer token to API calls in local mode and refreshes once on a 401. */
export const localAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const facade = inject(AUTH_FACADE);
  if (facade.mode !== 'local' || !req.url.startsWith(environment.apiBaseUrl)) return next(req);
  const local = facade as LocalAuthService;

  const withToken = (token: string) =>
    token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return from(local.getAccessToken()).pipe(
    switchMap((token) => next(withToken(token))),
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || req.url.includes('/auth/')) {
        return throwError(() => error);
      }
      return from(local.refresh()).pipe(
        switchMap((ok): Observable<never> | ReturnType<typeof next> => {
          if (!ok) {
            local.logout();
            return throwError(() => error);
          }
          return from(local.getAccessToken()).pipe(switchMap((token) => next(withToken(token))));
        }),
      );
    }),
  );
};

function decodeClaims(token: string | null): Record<string, string> {
  if (!token) return {};
  try {
    const payload = token.split('.')[1] ?? '';
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    const parsed = JSON.parse(json) as Record<string, unknown>;
    const out: Record<string, string> = {};
    for (const [k, v] of Object.entries(parsed)) if (typeof v === 'string') out[k] = v;
    return out;
  } catch {
    return {};
  }
}

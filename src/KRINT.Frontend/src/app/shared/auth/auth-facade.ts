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
 * OIDC keeps angular-auth-oidc-client underneath; local mode talks to the session endpoints
 * the API serves. Components only ever see this.
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
  const router = inject(Router);
  return local
    .ensureSession()
    .then((ok) => (ok ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })));
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

type SessionTokenResponse = { accessToken?: string; expiresIn?: number; error_description?: string };

/**
 * Local mode session. The refresh token never reaches this code: the API keeps it in an
 * HttpOnly cookie scoped to /auth/session, and this class only ever holds the short-lived
 * access token, in memory. A reload calls /auth/session/refresh with the cookie and is signed
 * in again before the first route renders.
 */
export class LocalAuthService implements AuthFacade {
  readonly mode: AuthMode = 'local';
  private readonly router = inject(Router);
  private readonly accessToken = signal<string | null>(null);
  private expiresAt = 0;
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

  /** True when a session exists or could be restored from the cookie. */
  async ensureSession(): Promise<boolean> {
    if (this.accessToken() && Date.now() < this.expiresAt - 30_000) return true;
    // The session cookie itself is HttpOnly; the API sets a readable marker beside it so a
    // browser that never signed in does not fire a refresh that can only answer 401.
    if (!document.cookie.split(';').some((c) => c.trim().startsWith('krint.session.present='))) return false;
    return this.refresh();
  }

  async getAccessToken(): Promise<string> {
    if (!this.accessToken() || Date.now() >= this.expiresAt - 30_000) await this.refresh();
    return this.accessToken() ?? '';
  }

  /** Returns null on success, otherwise the message to show. */
  async signIn(identifier: string, password: string): Promise<string | null> {
    const response = await fetch(`${environment.apiBaseUrl}/auth/session/login`, {
      method: 'POST',
      credentials: 'include',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ identifier, password }),
    });
    const body = (await response.json().catch(() => ({}))) as SessionTokenResponse;
    if (response.status === 401) return body.error_description ?? 'The credentials are not valid.';
    if (!response.ok) return `Sign-in failed (${response.status}).`;
    if (!body.accessToken) return 'The server returned no token.';
    this.store(body.accessToken, body.expiresIn ?? 900);
    return null;
  }

  /** Single-flight: the refresh token rotates on every use, so parallel refreshes look like theft. */
  refresh(): Promise<boolean> {
    this.refreshing ??= this.refreshOnce().finally(() => {
      this.refreshing = null;
    });
    return this.refreshing;
  }

  private async refreshOnce(): Promise<boolean> {
    try {
      const response = await fetch(`${environment.apiBaseUrl}/auth/session/refresh`, { method: 'POST', credentials: 'include' });
      if (!response.ok) {
        this.clear();
        return false;
      }
      const body = (await response.json()) as SessionTokenResponse;
      if (!body.accessToken) {
        this.clear();
        return false;
      }
      this.store(body.accessToken, body.expiresIn ?? 900);
      return true;
    } catch {
      return false;
    }
  }

  logout(): void {
    this.clear();
    void fetch(`${environment.apiBaseUrl}/auth/session/logout`, { method: 'POST', credentials: 'include' }).catch(() => undefined);
    void this.router.navigate(['/login']);
  }

  private store(access: string, expiresInSeconds: number): void {
    this.accessToken.set(access);
    this.expiresAt = Date.now() + expiresInSeconds * 1000;
  }

  private clear(): void {
    this.accessToken.set(null);
    this.expiresAt = 0;
  }
}

/** Adds the bearer token to API calls in local mode and refreshes once on a 401. */
export const localAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const facade = inject(AUTH_FACADE);
  if (facade.mode !== 'local' || !req.url.startsWith(environment.apiBaseUrl)) return next(req);
  const local = facade as LocalAuthService;

  const withToken = (token: string) =>
    token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` }, withCredentials: true }) : req;

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

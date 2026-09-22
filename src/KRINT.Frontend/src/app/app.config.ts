import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { authInterceptor, provideAuth, withAppInitializerAuthCheck } from 'angular-auth-oidc-client';

import { routes } from './app.routes';
import { provideApi } from './api/provide-api';
import { authLoaderProvider } from './shared/auth/auth.config';
import { AUTH_FACADE, AuthMode, LocalAuthService, OidcAuthFacade, localAuthInterceptor } from './shared/auth/auth-facade';
import { environment } from './shared/environments/environment';

/**
 * The provider set depends on how the backend authenticates, which main.ts reads from
 * /api/app before bootstrapping: OIDC installs angular-auth-oidc-client, local mode installs
 * the password-login session. Components only ever inject AUTH_FACADE.
 */
export function buildAppConfig(mode: AuthMode): ApplicationConfig {
  const shared = [provideBrowserGlobalErrorListeners(), provideRouter(routes), provideApi(environment.apiBaseUrl)];

  if (mode === 'local') {
    return {
      providers: [
        ...shared,
        provideHttpClient(withInterceptors([localAuthInterceptor])),
        { provide: AUTH_FACADE, useClass: LocalAuthService },
      ],
    };
  }

  return {
    providers: [
      ...shared,
      provideHttpClient(withInterceptors([authInterceptor()])),
      provideAuth({ loader: authLoaderProvider }, withAppInitializerAuthCheck()),
      { provide: AUTH_FACADE, useClass: OidcAuthFacade },
    ],
  };
}

/** Kept for anything that still imports the old constant; OIDC is the historical default. */
export const appConfig: ApplicationConfig = buildAppConfig('oidc');

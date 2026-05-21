import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { authInterceptor, provideAuth, withAppInitializerAuthCheck } from 'angular-auth-oidc-client';

import { routes } from './app.routes';
import { provideApi } from './api/provide-api';
import { authLoaderProvider } from './shared/auth/auth.config';
import { environment } from './shared/environments/environment';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor()])),
    provideApi(environment.apiBaseUrl),
    provideAuth({ loader: authLoaderProvider }, withAppInitializerAuthCheck()),
  ],
};

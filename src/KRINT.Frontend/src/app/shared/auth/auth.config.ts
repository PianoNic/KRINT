import { StsConfigHttpLoader, StsConfigLoader } from 'angular-auth-oidc-client';
import { map } from 'rxjs/operators';
import { AppService } from '../../api/api/app.service';
import { environment } from '../environments/environment';

export const authLoaderFactory = (appService: AppService) => {
  const config$ = appService.apiAppGet().pipe(
    map((app) => ({
      authority: app.authority ?? '',
      redirectUrl: app.redirectUri ?? '',
      postLogoutRedirectUri: app.postLogoutRedirectUri ?? '',
      clientId: app.clientId ?? '',
      scope: app.scope ?? '',
      responseType: 'code',
      silentRenew: true,
      useRefreshToken: true,
      renewTimeBeforeTokenExpiresInSeconds: 30,
      secureRoutes: [environment.apiBaseUrl],
    })),
  );
  return new StsConfigHttpLoader(config$);
};

export const authLoaderProvider = {
  provide: StsConfigLoader,
  useFactory: authLoaderFactory,
  deps: [AppService],
};

import { bootstrapApplication } from '@angular/platform-browser';
import { buildAppConfig } from './app/app.config';
import { App } from './app/app';
import { AuthMode } from './app/shared/auth/auth-facade';
import { environment } from './app/shared/environments/environment';

/**
 * One request before Angular starts: which sign-in the backend runs. OIDC needs its library
 * installed at bootstrap and local login must not have it, so the choice cannot wait for a
 * component. When the API cannot be reached at all, say so on the page instead of showing a
 * blank screen and a console error nobody sees.
 */
async function readAuthMode(): Promise<AuthMode> {
  const response = await fetch(`${environment.apiBaseUrl}/api/app`, { headers: { accept: 'application/json' } });
  if (!response.ok) throw new Error(`GET /api/app answered ${response.status}`);
  const app = (await response.json()) as { authMode?: string };
  return app.authMode === 'local' ? 'local' : 'oidc';
}

function showUnreachable(reason: string): void {
  document.body.innerHTML = '';
  const box = document.createElement('main');
  box.setAttribute('style', 'font-family:system-ui,sans-serif;max-width:40rem;margin:4rem auto;padding:0 1rem;line-height:1.5');
  const title = document.createElement('h1');
  title.textContent = 'KRINT cannot reach its API';
  const detail = document.createElement('p');
  detail.textContent = `The page loaded, but ${environment.apiBaseUrl}/api/app did not answer (${reason}). ` +
    'Check that the API is running, that this origin is in Cors__AllowedOrigins, and that Krint__PublicUrl matches the address in the browser.';
  box.append(title, detail);
  document.body.append(box);
}

readAuthMode()
  .then((mode) => bootstrapApplication(App, buildAppConfig(mode)))
  .catch((err: unknown) => {
    console.error(err);
    showUnreachable(err instanceof Error ? err.message : String(err));
  });

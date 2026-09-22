import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { AUTH_FACADE, LocalAuthService } from '../shared/auth/auth-facade';

/** Sign-in form for local password login. Never reached in OIDC mode. */
@Component({
  selector: 'app-login',
  imports: [HlmButtonImports, HlmCardImports, HlmInputImports, HlmLabelImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bg-background flex min-h-svh items-center justify-center p-4">
      <section hlmCard class="w-full max-w-sm">
        <div hlmCardHeader>
          <h1 hlmCardTitle>Sign in to KRINT</h1>
          <p hlmCardDescription>Local account on this KRINT instance.</p>
        </div>
        <form hlmCardContent class="flex flex-col gap-4" (submit)="submit($event)">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="identifier">User name</label>
            <input
              hlmInput
              id="identifier"
              name="identifier"
              autocomplete="username"
              autofocus
              [value]="identifier()"
              (input)="identifier.set($any($event.target).value)"
            />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="password">Password</label>
            <input
              hlmInput
              id="password"
              name="password"
              type="password"
              autocomplete="current-password"
              [value]="password()"
              (input)="password.set($any($event.target).value)"
            />
          </div>
          @if (error(); as err) {
            <p class="text-destructive text-sm" role="alert">{{ err }}</p>
          }
          <button hlmBtn type="submit" [disabled]="busy() || !identifier() || !password()">
            {{ busy() ? 'Signing in…' : 'Sign in' }}
          </button>
          <p class="text-muted-foreground text-xs">
            First run? The admin password is in the server log, or in <code class="font-mono">LocalLogin__AdminPassword</code>.
          </p>
        </form>
      </section>
    </div>
  `,
})
export class Login {
  private readonly auth = inject(AUTH_FACADE) as LocalAuthService;
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly identifier = signal('');
  protected readonly password = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // The desktop shell opens this page with its own account in the URL fragment
    // (#krint_desktop=<base64url user:password>). The fragment never reaches the server; it
    // is read once, cleared from the address bar, and used to sign in without a form.
    const fragment = new URLSearchParams(window.location.hash.replace(/^#/, ''));
    const desktop = fragment.get('krint_desktop');
    if (desktop) {
      history.replaceState(null, '', window.location.pathname + window.location.search);
      const decoded = decodeBase64Url(desktop);
      const separator = decoded.indexOf(':');
      if (separator > 0) {
        this.identifier.set(decoded.slice(0, separator));
        this.password.set(decoded.slice(separator + 1));
        void this.submit(new Event('desktop'));
      }
    }
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const message = await this.auth.signIn(this.identifier().trim(), this.password());
      if (message) {
        this.error.set(message);
        return;
      }
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
      await this.router.navigateByUrl(returnUrl.startsWith('/') ? returnUrl : '/');
    } catch {
      this.error.set('Could not reach the API.');
    } finally {
      this.busy.set(false);
    }
  }
}

function decodeBase64Url(value: string): string {
  try {
    const padded = value.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (value.length % 4)) % 4);
    const bytes = Uint8Array.from(atob(padded), (ch) => ch.charCodeAt(0));
    return new TextDecoder().decode(bytes);
  } catch {
    return '';
  }
}

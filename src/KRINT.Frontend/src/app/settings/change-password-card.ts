import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { AUTH_FACADE } from '../shared/auth/auth-facade';
import { environment } from '../shared/environments/environment';

/**
 * Change the signed-in account's password. Local login only: with an identity provider the
 * password lives there. A successful change revokes every session, including this one, so
 * the page signs the user out and back to the login form.
 */
@Component({
  selector: 'app-change-password-card',
  imports: [HlmButtonImports, HlmCardImports, HlmInputImports, HlmLabelImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (auth.mode === 'local') {
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>Password</h2>
          <p hlmCardDescription>Local account {{ auth.user().name }}. Changing it signs every session out, this one included.</p>
        </div>
        <form hlmCardContent class="flex max-w-sm flex-col gap-3" (submit)="submit($event)">
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="current-password">Current password</label>
            <input hlmInput id="current-password" type="password" autocomplete="current-password" [value]="current()" (input)="current.set($any($event.target).value)" />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="new-password">New password</label>
            <input hlmInput id="new-password" type="password" autocomplete="new-password" [value]="next()" (input)="next.set($any($event.target).value)" />
          </div>
          <div class="flex flex-col gap-1.5">
            <label hlmLabel for="confirm-password">Repeat new password</label>
            <input hlmInput id="confirm-password" type="password" autocomplete="new-password" [value]="confirm()" (input)="confirm.set($any($event.target).value)" />
          </div>
          @if (error(); as err) {
            <p class="text-destructive text-sm" role="alert">{{ err }}</p>
          }
          <div>
            <button hlmBtn type="submit" size="sm" [disabled]="!canSubmit()">
              {{ busy() ? 'Changing…' : 'Change password' }}
            </button>
          </div>
        </form>
      </section>
    }
  `,
})
export class ChangePasswordCard {
  protected readonly auth = inject(AUTH_FACADE);
  private readonly http = inject(HttpClient);

  protected readonly current = signal('');
  protected readonly next = signal('');
  protected readonly confirm = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly canSubmit = computed(
    () => !this.busy() && this.current().length > 0 && this.next().length >= 8 && this.next() === this.confirm(),
  );

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;
    this.busy.set(true);
    this.error.set(null);
    this.http
      .post(`${environment.apiBaseUrl}/auth/password`, { currentPassword: this.current(), newPassword: this.next() })
      .subscribe({
        next: () => {
          this.busy.set(false);
          // Every refresh token is now revoked, so the only honest next step is a fresh sign-in.
          this.auth.logout();
        },
        error: (err: unknown) => {
          this.busy.set(false);
          const body = err instanceof HttpErrorResponse ? (err.error as { errors?: string[]; error_description?: string } | null) : null;
          this.error.set(body?.errors?.[0] ?? body?.error_description ?? 'The password could not be changed.');
        },
      });
  }
}

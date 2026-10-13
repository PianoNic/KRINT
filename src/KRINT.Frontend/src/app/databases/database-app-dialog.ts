import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { CopyButton } from '../shared/components/copy-button/copy-button';
import { environment } from '../shared/environments/environment';

type DialogContext = { id: string; displayName: string; databaseTerm: string; hasUsers: boolean };

type AppDatabase = {
  database: string;
  username: string;
  password: string;
  hasScopedUser: boolean;
  appConnectionString?: string | null;
  connectionString: string;
};

/** "Give my app a database": one name in, one connection string out. */
@Component({
  selector: 'app-database-app-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription, HlmInputImports, CopyButton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>New app {{ ctx.databaseTerm }} on {{ ctx.displayName }}</h3>
      <p hlmDialogDescription>
        @if (ctx.hasUsers) {
          Creates a {{ ctx.databaseTerm }} and a user of the same name that can only reach it, and
          gives you the connection string for your app.
        } @else {
          Creates a {{ ctx.databaseTerm }} for your app and gives you its connection string.
        }
      </p>
    </hlm-dialog-header>

    @if (created(); as app) {
      <div class="flex flex-col gap-2" role="status">
        <span class="text-sm font-medium">Connection string for your app</span>
        <div class="flex items-start gap-2">
          <code class="bg-muted flex-1 break-all rounded-md p-3 font-mono text-xs">{{
            app.appConnectionString ?? app.connectionString
          }}</code>
          <app-copy-button [value]="app.appConnectionString ?? app.connectionString" />
        </div>
        <p class="text-muted-foreground text-xs">
          Paste it into your app's compose file. It works from any container on the same host,
          with no network config.
          @if (app.hasScopedUser) {
            The password is shown only now. If you lose it, reset the
            <span class="font-mono">{{ app.username }}</span> user's password from Edit.
          }
        </p>
      </div>
      <div class="flex justify-end">
        <button hlmBtn type="button" (click)="close()">Done</button>
      </div>
    } @else {
      <form class="flex flex-col gap-2" (submit)="$event.preventDefault(); create()">
        <label class="text-sm font-medium" for="app-db-name">App name</label>
        <input
          hlmInput
          id="app-db-name"
          placeholder="shop"
          autocomplete="off"
          [value]="name()"
          (input)="name.set($any($event.target).value)"
          [attr.aria-invalid]="!!error() || null"
          [attr.aria-describedby]="error() ? 'app-db-error' : 'app-db-hint'"
        />
        @if (error(); as err) {
          <span id="app-db-error" class="text-destructive text-sm" role="alert">{{ err }}</span>
        } @else {
          <span id="app-db-hint" class="text-muted-foreground text-xs">
            Letters, digits, _ and -. Used for the {{ ctx.databaseTerm }}
            @if (ctx.hasUsers) {
              and the user
            }
            name.
          </span>
        }
        <div class="mt-2 flex justify-end gap-2">
          <button hlmBtn variant="outline" type="button" (click)="close()">Cancel</button>
          <button hlmBtn type="submit" [disabled]="!canSubmit()">
            {{ busy() ? 'Creating...' : 'Create' }}
          </button>
        </div>
      </form>
    }
  `,
})
export class DatabaseAppDialog {
  protected readonly ctx = injectBrnDialogContext<DialogContext>();
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  private readonly http = inject(HttpClient);

  protected readonly name = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly created = signal<AppDatabase | null>(null);
  protected readonly canSubmit = computed(() => this.name().trim().length > 0 && !this.busy());

  protected create(): void {
    if (!this.canSubmit()) return;
    this.busy.set(true);
    this.error.set(null);
    this.http
      .post<AppDatabase>(`${environment.apiBaseUrl}/api/Database/${this.ctx.id}/apps`, { name: this.name().trim() })
      .subscribe({
        next: (app) => {
          this.busy.set(false);
          this.created.set(app);
        },
        error: (err: unknown) => {
          this.busy.set(false);
          this.error.set(messageOf(err));
        },
      });
  }

  protected close(): void {
    this.ref.close(this.created() !== null);
  }
}

function messageOf(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as { error?: string; detail?: string } | null;
    return body?.error ?? body?.detail ?? `Request failed (${err.status}).`;
  }
  return err instanceof Error ? err.message : 'Request failed.';
}

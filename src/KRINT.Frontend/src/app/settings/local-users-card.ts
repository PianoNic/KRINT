import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideKeyRound, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { Observable } from 'rxjs';
import { AUTH_FACADE } from '../shared/auth/auth-facade';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { CopyButton } from '../shared/components/copy-button/copy-button';
import { environment } from '../shared/environments/environment';

type LocalUser = { id: string; userName: string; email: string | null; createdAt: string; isCurrent: boolean };
type Credential = { id: string; userName: string; password: string };

/** Local login accounts. Only rendered in local mode; the API answers 404 otherwise. */
@Component({
  selector: 'app-local-users-card',
  imports: [DatePipe, NgIcon, HlmBadgeImports, HlmButtonImports, HlmCardImports, HlmInputImports, HlmTableImports, CopyButton],
  providers: [provideIcons({ lucideKeyRound, lucidePlus, lucideTrash2 })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (auth.mode === 'local') {
      <section hlmCard>
        <div hlmCardHeader>
          <h2 hlmCardTitle>Local accounts</h2>
          <p hlmCardDescription>Everyone listed here can do everything in KRINT. Passwords are shown once, when created or reset.</p>
        </div>
        <div hlmCardContent class="flex flex-col gap-3">
          @if (issued(); as c) {
            <div class="border-primary/30 bg-primary/5 flex flex-wrap items-center gap-2 rounded-md border p-3 text-sm" role="status">
              <span>Password for <code class="font-mono">{{ c.userName }}</code>:</span>
              <code class="font-mono">{{ c.password }}</code>
              <app-copy-button [value]="c.password" />
              <span class="text-muted-foreground text-xs">Save it now, it will not be shown again.</span>
            </div>
          }
          @if (error(); as err) {
            <p class="text-destructive text-sm" role="alert">{{ err }}</p>
          }
          <table hlmTable>
            <thead hlmTableHeader>
              <tr hlmTableRow>
                <th hlmTableHead>User</th>
                <th hlmTableHead>Created</th>
                <th hlmTableHead class="text-right">Actions</th>
              </tr>
            </thead>
            <tbody hlmTableBody>
              @for (u of users(); track u.id) {
                <tr hlmTableRow>
                  <td hlmTableCell class="font-mono">
                    {{ u.userName }}
                    @if (u.isCurrent) {
                      <span hlmBadge variant="secondary" class="ml-2 text-[10px]">you</span>
                    }
                  </td>
                  <td hlmTableCell class="text-muted-foreground text-xs">{{ u.createdAt | date: 'medium' }}</td>
                  <td hlmTableCell class="text-right">
                    <button hlmBtn variant="ghost" size="sm" type="button" (click)="reset(u)" [disabled]="busy()">
                      <ng-icon name="lucideKeyRound" size="14" /> Reset password
                    </button>
                    <button hlmBtn variant="ghost" size="sm" type="button" (click)="remove(u)" [disabled]="busy() || u.isCurrent">
                      <ng-icon name="lucideTrash2" size="14" /> Delete
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
          <form class="flex items-end gap-2" (submit)="create($event)">
            <div class="flex flex-col gap-1">
              <label class="text-muted-foreground text-xs" for="new-local-user">New account</label>
              <input hlmInput id="new-local-user" placeholder="user name" autocomplete="off" [value]="newName()" (input)="newName.set($any($event.target).value)" />
            </div>
            <button hlmBtn size="sm" type="submit" [disabled]="busy() || newName().trim() === ''">
              <ng-icon name="lucidePlus" size="14" /> Create
            </button>
          </form>
        </div>
      </section>
    }
  `,
})
export class LocalUsersCard {
  protected readonly auth = inject(AUTH_FACADE);
  private readonly http = inject(HttpClient);
  private readonly confirm = inject(ConfirmService);
  private readonly base = `${environment.apiBaseUrl}/api/local-users`;

  protected readonly users = signal<LocalUser[]>([]);
  protected readonly issued = signal<Credential | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly newName = signal('');

  constructor() {
    if (this.auth.mode === 'local') this.load();
  }

  private load(): void {
    this.http.get<LocalUser[]>(this.base).subscribe({
      next: (list) => this.users.set(list),
      error: (err: unknown) => this.error.set(messageOf(err)),
    });
  }

  protected create(event: Event): void {
    event.preventDefault();
    const userName = this.newName().trim();
    if (!userName) return;
    this.run(this.http.post<Credential>(this.base, { userName }), (c) => {
      this.issued.set(c);
      this.newName.set('');
    });
  }

  protected async reset(u: LocalUser): Promise<void> {
    const ok = await this.confirm.open({
      title: `Reset the password of ${u.userName}?`,
      message: 'A new password is generated and every session of that account is signed out.',
      confirmLabel: 'Reset password',
      destructive: true,
    });
    if (!ok) return;
    this.run(this.http.post<Credential>(`${this.base}/${u.id}/reset-password`, {}), (c) => this.issued.set(c));
  }

  protected async remove(u: LocalUser): Promise<void> {
    const ok = await this.confirm.open({
      title: `Delete account ${u.userName}?`,
      message: 'The account and its sessions are removed. Instances and backups are not affected.',
      confirmLabel: 'Delete account',
      destructive: true,
      requireTypedValue: u.userName,
    });
    if (!ok) return;
    this.run(this.http.delete<void>(`${this.base}/${u.id}`), () => undefined);
  }

  private run<T>(request: Observable<T>, onDone: (value: T) => void): void {
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: (value) => {
        this.busy.set(false);
        onDone(value);
        this.load();
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.error.set(messageOf(err));
      },
    });
  }
}

function messageOf(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error as { error?: string; errors?: string[] } | null;
    return body?.error ?? body?.errors?.[0] ?? `Request failed (${err.status}).`;
  }
  return err instanceof Error ? err.message : 'Request failed.';
}

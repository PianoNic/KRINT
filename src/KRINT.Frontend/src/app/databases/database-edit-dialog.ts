import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideKeyRound, lucideTrash2 } from '@ng-icons/lucide';
import { DatabasesStore } from '../shared/stores/databases.store';
import { CopyButton } from '../shared/components/copy-button/copy-button';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';

type DialogContext = { id: string; engine: string; containerName: string };

@Component({
  selector: 'app-database-edit-dialog',
  imports: [
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmInputImports,
    HlmLabelImports,
    HlmTabsImports,
    HlmTooltipImports,
    NgIcon,
    CopyButton,
  ],
  providers: [provideIcons({ lucideTrash2, lucideKeyRound })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>Edit {{ ctx.containerName }}</h3>
      <p hlmDialogDescription>
        Manage logical databases and users on this {{ ctx.engine }} instance.
      </p>
    </hlm-dialog-header>

    <hlm-tabs tab="databases" class="w-full">
      <hlm-tabs-list aria-label="Edit instance">
        <button hlmTabsTrigger="databases">Databases</button>
        <button hlmTabsTrigger="users">Users</button>
      </hlm-tabs-list>

      <div hlmTabsContent="databases" class="flex flex-col gap-3 pt-4">
        <form class="flex gap-2" (submit)="addDb($event)">
          <label class="sr-only" hlmLabel for="db-name">New database name</label>
          <input
            hlmInput
            id="db-name"
            class="flex-1"
            placeholder="my_database"
            [value]="newDbName()"
            (input)="newDbName.set($any($event.target).value)"
            [disabled]="store.mutatingInner()"
          />
          <button hlmBtn type="submit" [disabled]="!newDbName() || store.mutatingInner()">
            Add
          </button>
        </form>

        @if (store.loadingInner()) {
          <p class="text-muted-foreground text-sm">Loading…</p>
        } @else if (store.innerDatabases().length === 0) {
          <p class="text-muted-foreground text-sm">No databases yet inside this instance.</p>
        } @else {
          <ul class="divide-border divide-y rounded-md border">
            @for (name of store.innerDatabases(); track name) {
              <li class="flex items-center justify-between px-3 py-2">
                <span class="font-mono text-sm">{{ name }}</span>
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon"
                  type="button"
                  [attr.aria-label]="'Drop ' + name"
                  [hlmTooltip]="'Drop ' + name"
                  [disabled]="store.mutatingInner()"
                  (click)="dropDb(name)"
                >
                  <ng-icon name="lucideTrash2" size="16" />
                </button>
              </li>
            }
          </ul>
        }
      </div>

      <div hlmTabsContent="users" class="flex flex-col gap-3 pt-4">
        <form class="flex gap-2" (submit)="addUser($event)">
          <label class="sr-only" hlmLabel for="user-name">New user name</label>
          <input
            hlmInput
            id="user-name"
            class="flex-1"
            placeholder="alice"
            [value]="newUserName()"
            (input)="newUserName.set($any($event.target).value)"
            [disabled]="store.mutatingUsers()"
          />
          <button hlmBtn type="submit" [disabled]="!newUserName() || store.mutatingUsers()">
            Create
          </button>
        </form>

        @if (store.lastCredential(); as cred) {
          <div class="border-primary/30 bg-primary/5 flex items-start gap-2 rounded-md border p-3">
            <div class="flex-1 text-sm">
              <p class="font-medium">Save this password — it won't be shown again.</p>
              <p class="text-muted-foreground mt-1">
                User <code class="font-mono">{{ cred.name }}</code>
              </p>
              <code class="mt-2 block break-all font-mono text-xs">{{ cred.password }}</code>
            </div>
            <app-copy-button [value]="cred.password" />
            <button
              hlmBtn
              variant="ghost"
              size="icon"
              type="button"
              aria-label="Dismiss"
              hlmTooltip="Dismiss"
              (click)="store.clearLastCredential()"
            >
              ×
            </button>
          </div>
        }

        @if (store.loadingUsers()) {
          <p class="text-muted-foreground text-sm">Loading…</p>
        } @else if (store.users().length === 0) {
          <p class="text-muted-foreground text-sm">No users yet.</p>
        } @else {
          <ul class="divide-border divide-y rounded-md border">
            @for (name of store.users(); track name) {
              <li class="flex items-center justify-between px-3 py-2">
                <span class="font-mono text-sm">{{ name }}</span>
                <div class="flex gap-1">
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon"
                    type="button"
                    [attr.aria-label]="'Reset password for ' + name"
                    [hlmTooltip]="'Reset password for ' + name"
                    [disabled]="store.mutatingUsers()"
                    (click)="resetUser(name)"
                  >
                    <ng-icon name="lucideKeyRound" size="16" />
                  </button>
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon"
                    type="button"
                    [attr.aria-label]="'Delete user ' + name"
                    [hlmTooltip]="'Delete user ' + name"
                    [disabled]="store.mutatingUsers()"
                    (click)="deleteUser(name)"
                  >
                    <ng-icon name="lucideTrash2" size="16" />
                  </button>
                </div>
              </li>
            }
          </ul>
        }
      </div>
    </hlm-tabs>

    @if (store.error(); as err) {
      <p class="text-destructive text-sm">{{ err }}</p>
    }

    <div class="flex justify-end">
      <button hlmBtn variant="outline" (click)="close()">Close</button>
    </div>
  `,
})
export class DatabaseEditDialog {
  protected readonly store = inject(DatabasesStore);
  private readonly confirmService = inject(ConfirmService);
  protected readonly newDbName = signal('');
  protected readonly newUserName = signal('');
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  protected readonly ctx = injectBrnDialogContext<DialogContext>();

  constructor() {
    this.store.loadInner(this.ctx.id);
    this.store.loadUsers(this.ctx.id);
    effect((onCleanup) => {
      onCleanup(() => this.store.clearDetails());
    });
  }

  protected addDb(event: Event): void {
    event.preventDefault();
    const name = this.newDbName().trim();
    if (!name) return;
    this.store.createInner({ id: this.ctx.id, name });
    this.newDbName.set('');
  }

  protected async dropDb(name: string): Promise<void> {
    const ok = await this.confirmService.open({
      title: `Drop database "${name}"?`,
      message: 'Every table and row inside this logical database will be permanently deleted.',
      confirmLabel: 'Drop database',
      destructive: true,
    });
    if (!ok) return;
    this.store.dropInner({ id: this.ctx.id, name });
  }

  protected addUser(event: Event): void {
    event.preventDefault();
    const name = this.newUserName().trim();
    if (!name) return;
    this.store.createUser({ id: this.ctx.id, name });
    this.newUserName.set('');
  }

  protected async deleteUser(name: string): Promise<void> {
    const ok = await this.confirmService.open({
      title: `Delete user "${name}"?`,
      message: 'The user account is removed from the database instance.',
      confirmLabel: 'Delete user',
      destructive: true,
    });
    if (!ok) return;
    this.store.deleteUser({ id: this.ctx.id, name });
  }

  protected async resetUser(name: string): Promise<void> {
    const ok = await this.confirmService.open({
      title: `Reset password for "${name}"?`,
      message: 'A fresh password is generated. The current password stops working immediately and the new one is shown only once.',
      confirmLabel: 'Reset password',
      destructive: true,
    });
    if (!ok) return;
    this.store.resetUserPassword({ id: this.ctx.id, name });
  }

  protected close(): void {
    this.ref.close();
  }
}

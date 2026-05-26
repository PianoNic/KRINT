import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmTabsImports } from '@spartan-ng/helm/tabs';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideKeyRound, lucideTrash2 } from '@ng-icons/lucide';
import { DatabasesStore } from '../shared/stores/databases.store';
import { CopyButton } from '../shared/components/copy-button/copy-button';
import { ConfirmService } from '../shared/components/confirm-dialog/confirm-dialog';
import { SetPasswordService } from '../shared/components/set-password-dialog/set-password-dialog';

type DialogContext = { id: string; engine: string; containerName: string; displayName: string };

@Component({
  selector: 'app-database-edit-dialog',
  imports: [
    HlmButtonImports,
    HlmDialogHeader,
    HlmDialogTitle,
    HlmDialogDescription,
    HlmInputImports,
    HlmLabelImports,
    HlmSelectImports,
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
      <h3 hlmDialogTitle>Edit {{ displayName() || ctx.containerName }}</h3>
      <p hlmDialogDescription>
        Manage the name, logical databases, and users on this {{ ctx.engine }} instance.
      </p>
    </hlm-dialog-header>

    <!-- Rename form. Submits a PATCH on the instance; disabled until the value actually changes. -->
    <form class="flex items-end gap-2" (submit)="saveName($event)">
      <div class="flex flex-1 flex-col gap-1.5">
        <label hlmLabel for="display-name" class="text-muted-foreground text-xs uppercase tracking-wide">Name</label>
        <input
          hlmInput
          id="display-name"
          class="w-full"
          placeholder="e.g. Pangolin"
          [value]="displayName()"
          (input)="displayName.set($any($event.target).value)"
          [disabled]="renaming()"
        />
      </div>
      <button hlmBtn type="submit" size="sm" [disabled]="!canRename()">
        {{ renaming() ? 'Saving...' : 'Save name' }}
      </button>
    </form>

    <!-- Root password rotation. Gated on managed + stopped so the credential change
         can't race against live application traffic. The button explains itself when
         disabled. -->
    <div class="flex flex-col gap-2 rounded-md border p-3">
      <div class="flex items-center justify-between gap-2">
        <span class="text-sm font-medium">Root password</span>
        <button
          hlmBtn
          variant="outline"
          size="sm"
          type="button"
          [disabled]="!canEditRootPassword()"
          (click)="editRootPassword()"
        >
          <ng-icon name="lucideKeyRound" size="16" />
          Rotate
        </button>
      </div>
      @if (rootPasswordHint(); as hint) {
        <span class="text-muted-foreground text-xs">{{ hint }}</span>
      } @else {
        <span class="text-muted-foreground text-xs">
          The container is stopped and ready. KRINT will briefly start it, ALTER the credential,
          update the vault, and stop it again.
        </span>
      }
    </div>

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
        <form class="flex flex-col gap-2" (submit)="addUser($event)">
          <div class="flex gap-2">
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
            <label class="sr-only" hlmLabel for="user-password">Password (optional)</label>
            <input
              hlmInput
              id="user-password"
              class="flex-1"
              type="password"
              autocomplete="new-password"
              placeholder="auto-generated password"
              [value]="newUserPassword()"
              (input)="newUserPassword.set($any($event.target).value)"
              [disabled]="store.mutatingUsers()"
            />
            <button hlmBtn type="submit" [disabled]="!newUserName() || store.mutatingUsers()">
              Create
            </button>
          </div>
          <span class="text-muted-foreground text-xs">
            Leave the password blank to auto-generate. Allowed characters: A-Z, a-z, 0-9, and - _ . ~
          </span>
        </form>

        @if (store.lastCredential(); as cred) {
          <div class="border-primary/30 bg-primary/5 flex items-start gap-2 rounded-md border p-3">
            <div class="flex-1 text-sm">
              <p class="font-medium">Save this password - it won't be shown again.</p>
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
              <li class="flex items-center justify-between gap-2 px-3 py-2">
                <span class="font-mono text-sm">{{ name }}</span>
                <div class="flex items-center gap-1">
                  @if (store.innerDatabases().length > 0) {
                    <hlm-select
                      [value]="null"
                      (valueChange)="grantAccess(name, $event)"
                      [disabled]="store.mutatingUsers()"
                    >
                      <hlm-select-trigger size="sm" class="h-8 min-w-[10rem] text-xs">
                        <hlm-select-value placeholder="Grant access to…" />
                      </hlm-select-trigger>
                      <hlm-select-content *hlmSelectPortal>
                        @for (db of store.innerDatabases(); track db) {
                          <hlm-select-item [value]="db">{{ db }}</hlm-select-item>
                        }
                      </hlm-select-content>
                    </hlm-select>
                  }
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
  protected readonly newUserPassword = signal('');
  private readonly setPasswordService = inject(SetPasswordService);
  protected readonly displayName = signal('');
  protected readonly renaming = signal(false);
  private readonly ref = inject<BrnDialogRef<unknown>>(BrnDialogRef);
  protected readonly ctx = injectBrnDialogContext<DialogContext>();

  protected readonly canRename = computed(() => {
    const v = this.displayName().trim();
    return !this.renaming() && v.length > 0 && v.length <= 64 && v !== this.ctx.displayName;
  });

  // Root password edit is gated on managed + stopped. We look the instance up live so the
  // section reacts when the user starts/stops the DB from the parent list while this dialog
  // is open.
  protected readonly instance = computed(() => this.store.instances().find((d) => d.id === this.ctx.id));
  protected readonly canEditRootPassword = computed(() => {
    const i = this.instance();
    return !!i && i.isManaged && i.state === 'exited';
  });
  protected readonly rootPasswordHint = computed(() => {
    const i = this.instance();
    if (!i || !i.isManaged) return 'Root password rotation is only available for managed instances.';
    if (i.state !== 'exited') return `Stop the database first to edit the root password (current state: ${i.state ?? 'unknown'}).`;
    return null;
  });

  constructor() {
    this.displayName.set(this.ctx.displayName ?? this.ctx.containerName);
    this.store.loadInner(this.ctx.id);
    this.store.loadUsers(this.ctx.id);
    effect((onCleanup) => {
      onCleanup(() => this.store.clearDetails());
    });
  }

  protected saveName(event: Event): void {
    event.preventDefault();
    if (!this.canRename()) return;
    const name = this.displayName().trim();
    this.renaming.set(true);
    this.store.renameInstance({ id: this.ctx.id, displayName: name });
    // Stay disabled briefly so the user sees the click registered; clears as soon as the
    // store's instances refetch resolves (we re-read ctx via effect in the parent).
    setTimeout(() => this.renaming.set(false), 250);
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
    const password = this.newUserPassword().trim() || null;
    this.store.createUser({ id: this.ctx.id, name, password });
    this.newUserName.set('');
    this.newUserPassword.set('');
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

  protected grantAccess(user: string, database: string | null): void {
    if (!database) return;
    this.store.grantUserAccess({ id: this.ctx.id, name: user, database });
  }

  protected async resetUser(name: string): Promise<void> {
    const result = await this.setPasswordService.open({
      title: `Set password for "${name}"`,
      description: 'Leave blank to generate a fresh password. The current one stops working immediately; the new value is shown only once.',
      confirmLabel: 'Set password',
      destructive: true,
    });
    if (result === null) return; // cancelled
    this.store.resetUserPassword({ id: this.ctx.id, name, password: result || null });
  }

  protected async editRootPassword(): Promise<void> {
    if (!this.canEditRootPassword()) return;
    const result = await this.setPasswordService.open({
      title: 'Rotate root password',
      description: 'The database will briefly start to apply the change, then return to the stopped state. Leave blank to auto-generate.',
      confirmLabel: 'Rotate root password',
      destructive: true,
    });
    if (result === null) return;
    this.store.setRootPassword({ id: this.ctx.id, password: result || null });
  }

  protected close(): void {
    this.ref.close();
  }
}

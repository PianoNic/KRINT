import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withHooks, withMethods, withState, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, forkJoin, pipe, switchMap, tap } from 'rxjs';
import { DatabaseService } from '../../api/api/database.service';
import { CreateDatabaseDto } from '../../api/model/createDatabaseDto';
import { DatabaseInstanceDto } from '../../api/model/databaseInstanceDto';
import { InnerUserPasswordDto } from '../../api/model/innerUserPasswordDto';
import { ProvisionedDatabaseDto } from '../../api/model/provisionedDatabaseDto';
import { RegisterExternalDatabaseDto } from '../../api/model/registerExternalDatabaseDto';
import { SetVisibilityDto } from '../../api/model/setVisibilityDto';
import { SupportedDatabaseDto } from '../../api/model/supportedDatabaseDto';

type DatabasesState = {
  instances: ReadonlyArray<DatabaseInstanceDto>;
  supported: ReadonlyArray<SupportedDatabaseDto>;
  details: ProvisionedDatabaseDto | null;
  innerDatabases: ReadonlyArray<string>;
  users: ReadonlyArray<string>;
  lastCredential: InnerUserPasswordDto | null;
  loading: boolean;
  loadingDetails: boolean;
  loadingInner: boolean;
  loadingUsers: boolean;
  mutatingInner: boolean;
  mutatingUsers: boolean;
  deleting: string | null;
  creating: boolean;
  /// The id currently being started/stopped, if any. Drives per-row spinners.
  lifecycleBusy: string | null;
  error: string | null;
};

const initialState: DatabasesState = {
  instances: [],
  supported: [],
  details: null,
  innerDatabases: [],
  users: [],
  lastCredential: null,
  loading: false,
  loadingDetails: false,
  loadingInner: false,
  loadingUsers: false,
  mutatingInner: false,
  mutatingUsers: false,
  deleting: null,
  creating: false,
  lifecycleBusy: null,
  error: null,
};

export const DatabasesStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((store) => ({
    isEmpty: computed(() => !store.loading() && store.instances().length === 0),
  })),
  withMethods((store, api = inject(DatabaseService)) => ({
    load: rxMethod<void>(
      pipe(
        tap(() => patchState(store, { loading: true, error: null })),
        switchMap(() =>
          forkJoin({
            instances: api.apiDatabaseGet(),
            supported: api.apiDatabaseSupportedGet(),
          }).pipe(
            tap({
              next: ({ instances, supported }) =>
                patchState(store, { instances, supported, loading: false }),
              error: (err: unknown) =>
                patchState(store, { loading: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    loadDetails: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { loadingDetails: true, details: null, error: null })),
        switchMap((id) =>
          api.apiDatabaseIdGet(id).pipe(
            tap({
              next: (details) => patchState(store, { details, loadingDetails: false }),
              error: (err: unknown) =>
                patchState(store, { loadingDetails: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    clearDetails: () => patchState(store, { details: null, innerDatabases: [], users: [], lastCredential: null }),
    clearLastCredential: () => patchState(store, { lastCredential: null }),
    deleteInstance: rxMethod<string>(
      pipe(
        tap((id) => patchState(store, { deleting: id, error: null })),
        switchMap((id) =>
          api.apiDatabaseIdDelete(id).pipe(
            switchMap(() => api.apiDatabaseGet()),
            tap({
              next: (instances) => patchState(store, { instances, deleting: null }),
              error: (err: unknown) =>
                patchState(store, { deleting: null, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    loadUsers: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { loadingUsers: true, error: null })),
        switchMap((id) =>
          api.apiDatabaseIdUsersGet(id).pipe(
            tap({
              next: (users) => patchState(store, { users, loadingUsers: false }),
              error: (err: unknown) =>
                patchState(store, { loadingUsers: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    createUser: rxMethod<{ id: string; name: string; password?: string | null }>(
      pipe(
        tap(() => patchState(store, { mutatingUsers: true, error: null, lastCredential: null })),
        switchMap(({ id, name, password }) =>
          api.apiDatabaseIdUsersPost(id, { name, password: password || null }).pipe(
            switchMap((credential) =>
              api.apiDatabaseIdUsersGet(id).pipe(
                tap((users) => patchState(store, { users, mutatingUsers: false, lastCredential: credential })),
              ),
            ),
            tap({
              error: (err: unknown) =>
                patchState(store, { mutatingUsers: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    deleteUser: rxMethod<{ id: string; name: string }>(
      pipe(
        tap(() => patchState(store, { mutatingUsers: true, error: null })),
        switchMap(({ id, name }) =>
          api.apiDatabaseIdUsersNameDelete(id, name).pipe(
            switchMap(() => api.apiDatabaseIdUsersGet(id)),
            tap({
              next: (users) => patchState(store, { users, mutatingUsers: false }),
              error: (err: unknown) => {
                patchState(store, { mutatingUsers: false, error: messageOf(err) });
                return EMPTY;
              },
            }),
          ),
        ),
      ),
    ),
    // Reset (or set) a user's password. Empty/null password means auto-generate; backend
    // validates the alphabet via SafePasswordGuard.
    resetUserPassword: rxMethod<{ id: string; name: string; password?: string | null }>(
      pipe(
        tap(() => patchState(store, { mutatingUsers: true, error: null, lastCredential: null })),
        switchMap(({ id, name, password }) =>
          api.apiDatabaseIdUsersNameResetPasswordPost(id, name, { password: password || null }).pipe(
            tap({
              next: (credential) => patchState(store, { mutatingUsers: false, lastCredential: credential }),
              error: (err: unknown) =>
                patchState(store, { mutatingUsers: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    // Rotate the instance root password. The backend requires the container to be stopped
    // first; the UI gates this by checking db.state === 'exited'. Result credential is held
    // in lastCredential so the dialog can show it once.
    setRootPassword: rxMethod<{ id: string; password?: string | null }>(
      pipe(
        tap(() => patchState(store, { mutatingUsers: true, error: null, lastCredential: null })),
        switchMap(({ id, password }) =>
          api.apiDatabaseIdRootPasswordPost(id, { password: password || null }).pipe(
            tap({
              next: (credential) => patchState(store, { mutatingUsers: false, lastCredential: credential }),
              error: (err: unknown) =>
                patchState(store, { mutatingUsers: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    grantUserAccess: rxMethod<{ id: string; name: string; database: string }>(
      pipe(
        tap(() => patchState(store, { mutatingUsers: true, error: null })),
        switchMap(({ id, name, database }) =>
          api.apiDatabaseIdUsersNameGrantsPost(id, name, { database }).pipe(
            tap({
              next: () => patchState(store, { mutatingUsers: false }),
              error: (err: unknown) =>
                patchState(store, { mutatingUsers: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    // Rename an instance. PATCH returns 204; refetch the list so the new name surfaces
    // everywhere it's bound (the table, the left rails, etc.) without each caller knowing.
    renameInstance: rxMethod<{ id: string; displayName: string }>(
      pipe(
        tap(() => patchState(store, { error: null })),
        switchMap(({ id, displayName }) =>
          api.apiDatabaseIdPatch(id, { displayName }).pipe(
            switchMap(() => api.apiDatabaseGet()),
            tap({
              next: (instances) => patchState(store, { instances }),
              error: (err: unknown) => patchState(store, { error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    loadInner: rxMethod<string>(
      pipe(
        tap(() => patchState(store, { loadingInner: true, error: null })),
        switchMap((id) =>
          api.apiDatabaseIdDatabasesGet(id).pipe(
            tap({
              next: (innerDatabases) => patchState(store, { innerDatabases, loadingInner: false }),
              error: (err: unknown) =>
                patchState(store, { loadingInner: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    createInner: rxMethod<{ id: string; name: string }>(
      pipe(
        tap(() => patchState(store, { mutatingInner: true, error: null })),
        switchMap(({ id, name }) =>
          api.apiDatabaseIdDatabasesPost(id, { name }).pipe(
            switchMap(() => api.apiDatabaseIdDatabasesGet(id)),
            tap({
              next: (innerDatabases) => patchState(store, { innerDatabases, mutatingInner: false }),
              error: (err: unknown) =>
                patchState(store, { mutatingInner: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    dropInner: rxMethod<{ id: string; name: string }>(
      pipe(
        tap(() => patchState(store, { mutatingInner: true, error: null })),
        switchMap(({ id, name }) =>
          api.apiDatabaseIdDatabasesNameDelete(id, name).pipe(
            switchMap(() => api.apiDatabaseIdDatabasesGet(id)),
            tap({
              next: (innerDatabases) => patchState(store, { innerDatabases, mutatingInner: false }),
              error: (err: unknown) => {
                patchState(store, { mutatingInner: false, error: messageOf(err) });
                return EMPTY;
              },
            }),
          ),
        ),
      ),
    ),
    // Start/stop a container. Start blocks on readiness so the round-trip can take
    // 5-60s (or longer for JVM engines); lifecycleBusy drives the per-row spinner.
    startInstance: rxMethod<string>(
      pipe(
        tap((id) => patchState(store, { lifecycleBusy: id, error: null })),
        switchMap((id) =>
          api.apiDatabaseIdStartPost(id).pipe(
            switchMap(() => api.apiDatabaseGet().pipe(
              tap({
                next: (instances) => patchState(store, { instances, lifecycleBusy: null }),
              }),
            )),
            tap({
              error: (err: unknown) => patchState(store, { lifecycleBusy: null, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    stopInstance: rxMethod<string>(
      pipe(
        tap((id) => patchState(store, { lifecycleBusy: id, error: null })),
        switchMap((id) =>
          api.apiDatabaseIdStopPost(id).pipe(
            switchMap(() => api.apiDatabaseGet().pipe(
              tap({
                next: (instances) => patchState(store, { instances, lifecycleBusy: null }),
              }),
            )),
            tap({
              error: (err: unknown) => patchState(store, { lifecycleBusy: null, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    // Flip an instance between localhost-only and public. The backend tears down and
    // recreates the container in place, preserving the volume - takes a few seconds because
    // we wait for readiness before returning.
    setVisibility: rxMethod<{ id: string; dto: SetVisibilityDto; onResult?: (res: DatabaseInstanceDto | { error: string }) => void }>(
      pipe(
        tap(() => patchState(store, { error: null })),
        switchMap(({ id, dto, onResult }) =>
          api.apiDatabaseIdVisibilityPost(id, dto).pipe(
            switchMap((res) =>
              api.apiDatabaseGet().pipe(
                tap({
                  next: (instances) => {
                    patchState(store, { instances });
                    onResult?.(res);
                  },
                }),
              ),
            ),
            tap({
              error: (err: unknown) => {
                const msg = messageOf(err);
                patchState(store, { error: msg });
                onResult?.({ error: msg });
              },
            }),
          ),
        ),
      ),
    ),
    registerExternal: rxMethod<{ dto: RegisterExternalDatabaseDto; onResult?: (res: ProvisionedDatabaseDto | { error: string }) => void }>(
      pipe(
        tap(() => patchState(store, { creating: true, error: null })),
        switchMap(({ dto, onResult }) =>
          api.apiDatabaseRegisterPost(dto).pipe(
            switchMap((res) =>
              api.apiDatabaseGet().pipe(
                tap({
                  next: (instances) => {
                    patchState(store, { instances, creating: false });
                    onResult?.(res);
                  },
                }),
              ),
            ),
            tap({
              error: (err: unknown) => {
                const msg = messageOf(err);
                patchState(store, { creating: false, error: msg });
                onResult?.({ error: msg });
              },
            }),
          ),
        ),
      ),
    ),
    create: rxMethod<CreateDatabaseDto>(
      pipe(
        tap(() => patchState(store, { creating: true, error: null })),
        switchMap((dto) =>
          api.apiDatabasePost(dto).pipe(
            switchMap(() => api.apiDatabaseGet()),
            tap({
              next: (instances) => patchState(store, { instances, creating: false }),
              error: (err: unknown) =>
                patchState(store, { creating: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
  })),
  withHooks({
    onInit: (store) => store.load(),
  }),
);

function messageOf(err: unknown): string {
  if (err instanceof Error) return err.message;
  if (typeof err === 'object' && err !== null && 'message' in err) {
    return String((err as { message: unknown }).message);
  }
  return 'Request failed';
}

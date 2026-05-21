import { computed, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { signalStore, withComputed, withHooks, withMethods, withState, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { forkJoin, pipe, switchMap, tap } from 'rxjs';
import { DatabaseService } from '../../api/api/database.service';
import { CreateDatabaseDto } from '../../api/model/createDatabaseDto';
import { DatabaseInstanceDto } from '../../api/model/databaseInstanceDto';
import { ProvisionedDatabaseDto } from '../../api/model/provisionedDatabaseDto';
import { SupportedDatabaseDto } from '../../api/model/supportedDatabaseDto';
import { environment } from '../environments/environment';

type DatabasesState = {
  instances: ReadonlyArray<DatabaseInstanceDto>;
  supported: ReadonlyArray<SupportedDatabaseDto>;
  details: ProvisionedDatabaseDto | null;
  loading: boolean;
  loadingDetails: boolean;
  creating: boolean;
  error: string | null;
};

const initialState: DatabasesState = {
  instances: [],
  supported: [],
  details: null,
  loading: false,
  loadingDetails: false,
  creating: false,
  error: null,
};

export const DatabasesStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed((store) => ({
    isEmpty: computed(() => !store.loading() && store.instances().length === 0),
  })),
  withMethods((store, api = inject(DatabaseService), http = inject(HttpClient)) => ({
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
          http.get<ProvisionedDatabaseDto>(`${environment.apiBaseUrl}/api/Database/${id}`).pipe(
            tap({
              next: (details) => patchState(store, { details, loadingDetails: false }),
              error: (err: unknown) =>
                patchState(store, { loadingDetails: false, error: messageOf(err) }),
            }),
          ),
        ),
      ),
    ),
    clearDetails: () => patchState(store, { details: null }),
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

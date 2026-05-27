import { Injectable, inject } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel,
} from '@microsoft/signalr';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { Observable } from 'rxjs';
import { firstValueFrom } from 'rxjs';
import { environment } from '../shared/environments/environment';
import { MigrationProgressDto } from '../api/model/migrationProgressDto';
import { MigrationRequestDto } from '../api/model/migrationRequestDto';

/**
 * Wraps the SignalR `/hubs/migration` endpoint. Mirrors DashboardHubService: one shared
 * connection per app session, opened lazily. `stream` returns an Observable of progress
 * events; unsubscribing from it cancels the server-side migration via cancellation token.
 */
@Injectable({ providedIn: 'root' })
export class MigrationHubService {
  private readonly oidc = inject(OidcSecurityService);
  private connection: HubConnection | null = null;
  private connectPromise: Promise<HubConnection> | null = null;

  async getConnection(): Promise<HubConnection> {
    if (this.connection?.state === HubConnectionState.Connected) return this.connection;
    if (this.connectPromise) return this.connectPromise;

    this.connectPromise = (async () => {
      const conn = new HubConnectionBuilder()
        .withUrl(`${environment.apiBaseUrl}/hubs/migration`, {
          accessTokenFactory: async () => (await firstValueFrom(this.oidc.getAccessToken())) ?? '',
          transport: HttpTransportType.WebSockets,
          skipNegotiation: true,
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();
      await conn.start();
      this.connection = conn;
      return conn;
    })();

    try { return await this.connectPromise; }
    finally { this.connectPromise = null; }
  }

  stream(request: MigrationRequestDto): Observable<MigrationProgressDto> {
    return new Observable<MigrationProgressDto>((subscriber) => {
      let cancelled = false;
      let subscription: { dispose(): void } | null = null;

      (async () => {
        try {
          const conn = await this.getConnection();
          if (cancelled) return;
          subscription = conn.stream<MigrationProgressDto>('StreamMigration', request).subscribe({
            next: (value) => subscriber.next(value),
            error: (err) => subscriber.error(err),
            complete: () => subscriber.complete(),
          });
        } catch (err) {
          if (!cancelled) subscriber.error(err);
        }
      })();

      return () => {
        cancelled = true;
        try { subscription?.dispose(); } catch { /* already gone */ }
      };
    });
  }
}

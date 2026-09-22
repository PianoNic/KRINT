import { Injectable, inject } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel,
} from '@microsoft/signalr';
import { Observable } from 'rxjs';
import { AUTH_FACADE } from '../shared/auth/auth-facade';
import { environment } from '../shared/environments/environment';
import { ProvisionProgressDto } from '../api/model/provisionProgressDto';
import { ProvisionRequestDto } from '../api/model/provisionRequestDto';

/**
 * Wraps the SignalR `/hubs/provision` endpoint the create wizard streams from. Same shape as
 * MigrationHubService: one lazily opened connection, an Observable per provision, and
 * unsubscribing cancels the server side.
 */
@Injectable({ providedIn: 'root' })
export class ProvisionHubService {
  private readonly auth = inject(AUTH_FACADE);
  private connection: HubConnection | null = null;
  private connectPromise: Promise<HubConnection> | null = null;

  async getConnection(): Promise<HubConnection> {
    if (this.connection?.state === HubConnectionState.Connected) return this.connection;
    if (this.connectPromise) return this.connectPromise;

    this.connectPromise = (async () => {
      const conn = new HubConnectionBuilder()
        .withUrl(`${environment.apiBaseUrl}/hubs/provision`, {
          accessTokenFactory: async () => await this.auth.getAccessToken(),
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

    try {
      return await this.connectPromise;
    } finally {
      this.connectPromise = null;
    }
  }

  stream(request: ProvisionRequestDto): Observable<ProvisionProgressDto> {
    return new Observable<ProvisionProgressDto>((subscriber) => {
      let cancelled = false;
      let subscription: { dispose(): void } | null = null;

      (async () => {
        try {
          const conn = await this.getConnection();
          if (cancelled) return;
          subscription = conn.stream<ProvisionProgressDto>('StreamProvision', request).subscribe({
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
        try {
          subscription?.dispose();
        } catch {
          /* already gone */
        }
      };
    });
  }
}

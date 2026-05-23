import { Injectable, inject } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel,
} from '@microsoft/signalr';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom } from 'rxjs';
import { environment } from '../shared/environments/environment';

@Injectable({ providedIn: 'root' })
export class DashboardHubService {
  private readonly oidc = inject(OidcSecurityService);
  private connection: HubConnection | null = null;
  private connectPromise: Promise<HubConnection> | null = null;

  async getConnection(): Promise<HubConnection> {
    if (this.connection?.state === HubConnectionState.Connected) return this.connection;
    if (this.connectPromise) return this.connectPromise;

    this.connectPromise = (async () => {
      const conn = new HubConnectionBuilder()
        .withUrl(`${environment.apiBaseUrl}/hubs/dashboard`, {
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

  async stop(): Promise<void> {
    if (this.connection) {
      try { await this.connection.stop(); } catch { /* already gone */ }
      this.connection = null;
    }
  }
}

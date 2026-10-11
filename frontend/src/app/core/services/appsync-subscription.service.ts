import { Injectable, inject, signal, DestroyRef } from '@angular/core';
import { Observable, Subject, retry, timer } from 'rxjs';
import { webSocket, WebSocketSubject } from 'rxjs/webSocket';
import { WebhookEvent } from '../models';
import { environment } from '../../../environments/environment';

export interface AppSyncMessage {
  id?: string;
  type: 'connection_init' | 'connection_ack' | 'start' | 'data' | 'ka' | 'error' | 'stop' | 'complete';
  payload?: any;
}

@Injectable({ providedIn: 'root' })
export class AppSyncSubscriptionService {
  private readonly destroyRef = inject(DestroyRef);

  private readonly httpUrl = environment.appSync.graphqlUrl;
  private readonly apiKey = environment.appSync.apiKey;

  private socket$?: WebSocketSubject<any>;
  private readonly eventsSubject$ = new Subject<WebhookEvent>();
  private readonly criticalEventsSubject$ = new Subject<WebhookEvent>();

  /** Signal exposing connection state for dashboard UI indicators */
  readonly isConnected = signal<boolean>(false);

  constructor() {
    this.initWebSocket();

    // Clean up socket when service is destroyed
    this.destroyRef.onDestroy(() => {
      this.disconnect();
    });
  }

  /**
   * Returns an Observable stream of all real-time webhook events.
   */
  subscribeToEvents(): Observable<WebhookEvent> {
    return this.eventsSubject$.asObservable();
  }

  /**
   * Returns an Observable stream of only CRITICAL incidents.
   */
  subscribeToCriticalIncidents(): Observable<WebhookEvent> {
    return this.criticalEventsSubject$.asObservable();
  }

  /**
   * Manually re-initializes or reconnects the WebSocket connection.
   */
  reconnect(): void {
    this.disconnect();
    this.initWebSocket();
  }

  /**
   * Disconnects and closes the active socket.
   */
  disconnect(): void {
    this.isConnected.set(false);
    if (this.socket$) {
      try {
        this.socket$.complete();
      } catch {
        // ignore already closed
      }
      this.socket$ = undefined;
    }
  }

  private initWebSocket(): void {
    if (!this.httpUrl) {
      return;
    }

    try {
      const wsUrl = this.buildRealtimeUrl(this.httpUrl, this.apiKey);

      this.socket$ = webSocket({
        url: wsUrl,
        protocol: 'graphql-ws',
        openObserver: {
          next: () => {
            this.sendConnectionInit();
          }
        },
        closeObserver: {
          next: () => {
            this.isConnected.set(false);
          }
        }
      });

      this.socket$
        .pipe(
          // Automatic exponential backoff reconnection (1s, 2s, 4s, capped at 30s)
          retry({
            delay: (_errors, retryCount) => {
              const delayMs = Math.min(1000 * Math.pow(2, retryCount), 30000);
              return timer(delayMs);
            }
          })
        )
        .subscribe({
          next: (msg: AppSyncMessage) => this.handleMessage(msg),
          error: (err) => {
            this.isConnected.set(false);
            console.warn('[AppSync WebSocket Warning]', err);
          }
        });
    } catch (err) {
      this.isConnected.set(false);
      console.warn('[AppSync Initialization Failed]', err);
    }
  }

  private handleMessage(msg: AppSyncMessage): void {
    if (msg.type === 'connection_ack') {
      this.isConnected.set(true);
      this.registerSubscription();
      return;
    }

    if (msg.type === 'ka') {
      this.isConnected.set(true);
      return;
    }

    if (msg.type === 'data') {
      const event: WebhookEvent | undefined = msg.payload?.data?.onNewEvent;
      if (event) {
        this.eventsSubject$.next(event);
        if (event.severity === 'CRITICAL') {
          this.criticalEventsSubject$.next(event);
        }
      }
    }
  }

  private sendConnectionInit(): void {
    this.socket$?.next({ type: 'connection_init' });
  }

  private registerSubscription(): void {
    const subQuery = `
      subscription OnNewEvent {
        onNewEvent {
          id
          endpointId
          timestamp
          statusCode
          latencyMs
          severity
          status
          errorMessage
          payloadSnippet
        }
      }
    `;

    try {
      const host = new URL(this.httpUrl).host;
      this.socket$?.next({
        id: 'sub-active-incidents',
        type: 'start',
        payload: {
          data: JSON.stringify({ query: subQuery }),
          extensions: {
            authorization: {
              'x-api-key': this.apiKey,
              host
            }
          }
        }
      });
    } catch {
      // Handle invalid URL gracefully
    }
  }

  private buildRealtimeUrl(httpUrl: string, apiKey: string): string {
    const url = new URL(httpUrl);
    const host = url.host.replace('appsync-api', 'appsync-realtime-api');
    const authHeader = btoa(JSON.stringify({ host: url.host, 'x-api-key': apiKey }));
    const payloadHeader = btoa(JSON.stringify({}));
    const protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';

    return `${protocol}//${host}${url.pathname}?header=${authHeader}&payload=${payloadHeader}`;
  }
}

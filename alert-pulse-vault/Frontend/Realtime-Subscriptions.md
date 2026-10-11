---
title: "WebSocket Subscriptions (AWS AppSync)"
tags:
  - frontend
  - angular
  - appsync
  - websocket
  - rxjs
  - realtime
date: 2026-10-11
status: approved
---

# WebSocket Subscriptions (AWS AppSync)

This document presents the Angular client service responsible for maintaining real-time WebSocket subscriptions to AWS AppSync.

---

## 🔌 Connection Strategy

Alert Pulse supports two primary integration approaches for AppSync WebSockets:
1. **Native RxJS WebSocket (Recommended):** Zero heavy dependencies, implementing the lightweight `aws-appsync-realtime-api` protocol directly.
2. **AWS Amplify SDK (`@aws-amplify/api`):** Recommended if the project already uses the full AWS Amplify ecosystem and Cognito authentication.

The reference implementation below uses the native **RxJS** approach.

---

## 💻 Source Code: `appsync-subscription.service.ts`

```typescript
import { Injectable, inject, signal, DestroyRef } from '@angular/core';
import { Observable, Subject, filter, map, retry, timer } from 'rxjs';
import { webSocket, WebSocketSubject } from 'rxjs/webSocket';
import { WebhookEvent } from './monitoring.service';

interface AppSyncMessage {
  id?: string;
  type: 'connection_ack' | 'data' | 'ka' | 'error';
  payload?: any;
}

@Injectable({ providedIn: 'root' })
export class AppSyncSubscriptionService {
  private readonly destroyRef = inject(DestroyRef);

  // AppSync Configuration
  private readonly httpUrl = 'https://localhost:4566/graphql';
  private readonly apiKey = 'da2-test-api-key';

  private socket$?: WebSocketSubject<any>;
  private readonly eventsSubject$ = new Subject<WebhookEvent>();

  /** Signal exposing connection state for dashboard UI indicators */
  readonly isConnected = signal<boolean>(false);

  constructor() {
    this.initWebSocket();

    // Clean up socket when service is destroyed
    this.destroyRef.onDestroy(() => {
      this.socket$?.complete();
    });
  }

  /**
   * Returns an Observable stream of real-time webhook events.
   */
  subscribeToEvents(): Observable<WebhookEvent> {
    return this.eventsSubject$.asObservable();
  }

  private initWebSocket(): void {
    const wsUrl = this.buildRealtimeUrl(this.httpUrl, this.apiKey);

    this.socket$ = webSocket({
      url: wsUrl,
      openObserver: {
        next: () => {
          this.isConnected.set(true);
          this.sendConnectionInit();
          this.registerSubscription();
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
        // Automatic exponential backoff reconnection
        retry({
          delay: (errors, retryCount) => {
            const delayMs = Math.min(1000 * Math.pow(2, retryCount), 30000);
            return timer(delayMs);
          }
        })
      )
      .subscribe({
        next: (msg: AppSyncMessage) => this.handleMessage(msg),
        error: (err) => console.error('[AppSync WebSocket Error]', err)
      });
  }

  private handleMessage(msg: AppSyncMessage): void {
    if (msg.type === 'data' && msg.payload?.data?.onNewEvent) {
      const event: WebhookEvent = msg.payload.data.onNewEvent;
      this.eventsSubject$.next(event);
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

    this.socket$?.next({
      id: 'sub-active-incidents',
      type: 'start',
      payload: {
        data: JSON.stringify({ query: subQuery }),
        extensions: {
          authorization: {
            'x-api-key': this.apiKey,
            host: new URL(this.httpUrl).host
          }
        }
      }
    });
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
```

---

## 📡 WebSocket Lifecycle Events

| Socket Event | Service Action | Dashboard Effect |
| :--- | :--- | :--- |
| **`open`** | Sends `connection_init` and registers subscription | `isConnected = true` (Green Indicator) |
| **`data`** | Emits event via RxJS `Subject` | New row prepended in real time |
| **`ka`** | Internal heartbeat | Keeps socket session active |
| **`close / error`** | Triggers exponential backoff reconnection | `isConnected = false` (Amber/Red Indicator) |

---

## 🔗 Related Notes
* Backend mutation trigger: [[Service-Implementations|C# Service Implementations]].
* Consuming dashboard component: [[Dashboard-Component|Operational Dashboard Component]].

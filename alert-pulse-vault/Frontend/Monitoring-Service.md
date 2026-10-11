---
title: "Telemetry & State Service (Angular Signals)"
tags:
  - frontend
  - angular
  - signals
  - service
  - typescript
date: 2026-10-11
status: approved
---

# Telemetry & State Service (Angular Signals)

The `MonitoringService` serves as the primary state hub for the Angular client. It couples .NET REST API actions with a reactive **Signals** circular buffer to deliver smooth, stutter-free real-time telemetry updates.

---

## 💻 Source Code: `monitoring.service.ts`

```typescript
import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

export type SeverityLevel = 'INFO' | 'WARNING' | 'CRITICAL';
export type IncidentStatus = 'OPEN' | 'ACKNOWLEDGED' | 'RESOLVED';

export interface WebhookEvent {
  id: string;
  endpointId: string;
  timestamp: string;
  statusCode: number;
  latencyMs: number;
  severity: SeverityLevel;
  status: IncidentStatus;
  payloadSnippet?: string;
  errorMessage?: string;
}

@Injectable({ providedIn: 'root' })
export class MonitoringService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = 'http://localhost:5000/api/v1';

  // --- STATE SIGNALS ---
  
  /** In-memory circular buffer of the most recent 50 events */
  readonly activeIncidents = signal<WebhookEvent[]>([]);

  // --- COMPUTED SIGNALS (Metrics) ---
  
  /** Count of open incidents */
  readonly openCount = computed(() => 
    this.activeIncidents().filter(e => e.status === 'OPEN').length
  );

  /** Count of open CRITICAL incidents */
  readonly criticalCount = computed(() => 
    this.activeIncidents().filter(e => e.severity === 'CRITICAL' && e.status === 'OPEN').length
  );

  /** Average latency across active events (in ms) */
  readonly averageLatency = computed(() => {
    const events = this.activeIncidents();
    if (events.length === 0) return 0;
    const sum = events.reduce((acc, curr) => acc + curr.latencyMs, 0);
    return Math.round(sum / events.length);
  });

  // --- LOCAL STATE MUTATIONS ---

  /**
   * Appends an incoming WebSocket event to the top of the buffer,
   * capping the list at 50 items to minimize DOM overhead.
   */
  appendLiveEvent(event: WebhookEvent): void {
    this.activeIncidents.update(events => [event, ...events.slice(0, 49)]);
  }

  /**
   * Updates an incident status immutably in local state.
   */
  updateIncidentStatus(eventId: string, newStatus: IncidentStatus): void {
    this.activeIncidents.update(events =>
      events.map(e => (e.id === eventId ? { ...e, status: newStatus } : e))
    );
  }

  // --- OPERATIONAL ACTIONS (.NET REST API) ---

  /** Triggers an end-to-end pipeline simulation */
  simulateWebhook(endpointId: string, simulateError: boolean): Observable<{ eventId: string }> {
    return this.http.post<{ eventId: string }>(`${this.apiUrl}/webhooks/simulate`, {
      endpointId,
      statusCode: simulateError ? 500 : 200,
      payload: JSON.stringify({ event: 'ping', test: true, timestamp: new Date().toISOString() }),
      latencyMs: simulateError ? 3400 : 110,
      errorMessage: simulateError ? 'Simulated Gateway Timeout' : null
    });
  }

  /** Acknowledges an active incident */
  acknowledgeIncident(endpointId: string, eventId: string): Observable<void> {
    return this.http
      .post<void>(`${this.apiUrl}/incidents/${endpointId}/${eventId}/acknowledge`, {})
      .pipe(
        tap(() => this.updateIncidentStatus(eventId, 'ACKNOWLEDGED'))
      );
  }

  /** Replays a stored payload against the destination endpoint */
  retryPayload(endpointId: string, eventId: string): Observable<{ status: string }> {
    return this.http.post<{ status: string }>(
      `${this.apiUrl}/incidents/${endpointId}/${eventId}/retry`,
      {}
    );
  }
}
```

---

## 📈 Advantages of Signal-Based Circular Buffering

1. **Controlled Memory Footprint:** Limiting the active list to 50 items (`slice(0, 49)`) prevents browser memory bloat even during high-frequency webhook bursts.
2. **Instant Reactivity:** `computed()` signals only evaluate when `activeIncidents` changes, keeping dashboard counters and charts in sync with zero polling overhead.
3. **Immutability:** The functional `update(...)` pattern triggers fine-grained Angular change detection without expensive deep-object comparisons.

---

## 🔗 Related Notes
* How real-time events feed this service: [[Realtime-Subscriptions|WebSocket Subscriptions]].
* Rendering component: [[Dashboard-Component|Operational Dashboard Component]].

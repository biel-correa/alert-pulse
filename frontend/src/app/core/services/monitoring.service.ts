import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { WebhookEvent, IncidentStatus, EndpointConfig, CreateEndpointPayload } from '../models';
import { environment } from '../../../environments/environment';

export interface SimulateResponse {
  eventId: string;
  status: string;
  severity: string;
}

export interface AcknowledgeResponse {
  eventId: string;
  status: string;
  acknowledgedAt: string;
}

export interface RetryResponse {
  status: string;
  eventId: string;
  targetResponseStatusCode: number;
}

@Injectable({ providedIn: 'root' })
export class MonitoringService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = environment.apiUrl;

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
    this.activeIncidents.update(events => {
      // Check if item already exists (e.g. from prior mutation)
      const existingIdx = events.findIndex(e => e.id === event.id);
      if (existingIdx !== -1) {
        const copy = [...events];
        copy[existingIdx] = { ...copy[existingIdx], ...event };
        return copy;
      }
      return [event, ...events.slice(0, 49)];
    });
  }

  /**
   * Updates an incident status immutably in local state.
   */
  updateIncidentStatus(eventId: string, newStatus: IncidentStatus): void {
    this.activeIncidents.update(events =>
      events.map(e => (e.id === eventId ? { ...e, status: newStatus } : e))
    );
  }

  /**
   * Replaces current incidents with a snapshot.
   */
  setInitialIncidents(events: WebhookEvent[]): void {
    this.activeIncidents.set(events.slice(0, 50));
  }

  // --- OPERATIONAL ACTIONS (.NET REST API) ---

  /**
   * Triggers an end-to-end pipeline simulation.
   */
  simulateWebhook(endpointId: string, simulateError: boolean): Observable<SimulateResponse> {
    return this.http.post<SimulateResponse>(`${this.apiUrl}/webhooks/simulate`, {
      endpointId,
      statusCode: simulateError ? 500 : 200,
      payload: JSON.stringify({
        event: 'ping',
        test: true,
        timestamp: new Date().toISOString()
      }),
      latencyMs: simulateError ? 3400 : 110,
      errorMessage: simulateError ? 'Simulated Gateway Timeout' : null
    });
  }

  /**
   * Acknowledges an active incident in DynamoDB and updates local state.
   */
  acknowledgeIncident(endpointId: string, eventId: string): Observable<AcknowledgeResponse> {
    return this.http
      .post<AcknowledgeResponse>(`${this.apiUrl}/incidents/${endpointId}/${eventId}/acknowledge`, {})
      .pipe(
        tap(() => this.updateIncidentStatus(eventId, 'ACKNOWLEDGED'))
      );
  }

  /**
   * Replays a stored payload against the destination endpoint.
   */
  retryPayload(endpointId: string, eventId: string): Observable<RetryResponse> {
    return this.http.post<RetryResponse>(
      `${this.apiUrl}/incidents/${endpointId}/${eventId}/retry`,
      {}
    );
  }

  /**
   * Lists all registered endpoints from the backend.
   */
  getEndpoints(): Observable<EndpointConfig[]> {
    return this.http.get<EndpointConfig[]>(`${this.apiUrl}/endpoints`);
  }

  /**
   * Registers a new endpoint with the backend.
   */
  createEndpoint(payload: CreateEndpointPayload): Observable<EndpointConfig> {
    return this.http.post<EndpointConfig>(`${this.apiUrl}/endpoints`, payload);
  }
}

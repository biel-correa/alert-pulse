import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { MonitoringService } from './monitoring.service';
import { WebhookEvent } from '../models';
import { environment } from '../../../environments/environment';

describe('MonitoringService', () => {
  let service: MonitoringService;
  let httpMock: HttpTestingController;

  const mockEvent1: WebhookEvent = {
    id: 'evt-1',
    endpointId: 'stripe-billing',
    timestamp: '2026-10-11T10:00:00Z',
    statusCode: 200,
    latencyMs: 120,
    severity: 'INFO',
    status: 'OPEN'
  };

  const mockEvent2: WebhookEvent = {
    id: 'evt-2',
    endpointId: 'stripe-billing',
    timestamp: '2026-10-11T10:01:00Z',
    statusCode: 500,
    latencyMs: 3400,
    severity: 'CRITICAL',
    status: 'OPEN',
    errorMessage: 'Gateway Timeout'
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [MonitoringService]
    });

    service = TestBed.inject(MonitoringService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created with initial empty state', () => {
    expect(service).toBeTruthy();
    expect(service.activeIncidents().length).toBe(0);
    expect(service.openCount()).toBe(0);
    expect(service.criticalCount()).toBe(0);
    expect(service.averageLatency()).toBe(0);
  });

  it('should append live event and calculate computed metrics accurately', () => {
    service.appendLiveEvent(mockEvent1);
    service.appendLiveEvent(mockEvent2);

    expect(service.activeIncidents().length).toBe(2);
    // Newest event should be at the front
    expect(service.activeIncidents()[0].id).toBe('evt-2');
    expect(service.activeIncidents()[1].id).toBe('evt-1');

    expect(service.openCount()).toBe(2);
    expect(service.criticalCount()).toBe(1);
    // Average of 120 and 3400 is 1760
    expect(service.averageLatency()).toBe(1760);
  });

  it('should cap circular buffer at 50 events', () => {
    for (let i = 1; i <= 60; i++) {
      service.appendLiveEvent({
        id: `evt-${i}`,
        endpointId: 'test-endpoint',
        timestamp: new Date().toISOString(),
        statusCode: 200,
        latencyMs: 100,
        severity: 'INFO',
        status: 'OPEN'
      });
    }

    expect(service.activeIncidents().length).toBe(50);
    expect(service.activeIncidents()[0].id).toBe('evt-60');
    expect(service.activeIncidents()[49].id).toBe('evt-11');
  });

  it('should update incident status in place and adjust computed signals', () => {
    service.appendLiveEvent(mockEvent2);
    expect(service.openCount()).toBe(1);
    expect(service.criticalCount()).toBe(1);

    service.updateIncidentStatus('evt-2', 'ACKNOWLEDGED');

    expect(service.activeIncidents()[0].status).toBe('ACKNOWLEDGED');
    expect(service.openCount()).toBe(0);
    expect(service.criticalCount()).toBe(0);
  });

  it('should call simulateWebhook API endpoint', () => {
    service.simulateWebhook('stripe-billing', true).subscribe(res => {
      expect(res.eventId).toBe('sim-123');
      expect(res.status).toBe('SIMULATED');
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/webhooks/simulate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.endpointId).toBe('stripe-billing');
    expect(req.request.body.statusCode).toBe(500);

    req.flush({ eventId: 'sim-123', status: 'SIMULATED', severity: 'CRITICAL' });
  });

  it('should call acknowledgeIncident and update status in local state', () => {
    service.appendLiveEvent(mockEvent2);

    service.acknowledgeIncident('stripe-billing', 'evt-2').subscribe(res => {
      expect(res.status).toBe('ACKNOWLEDGED');
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/incidents/stripe-billing/evt-2/acknowledge`);
    expect(req.request.method).toBe('POST');

    req.flush({ eventId: 'evt-2', status: 'ACKNOWLEDGED', acknowledgedAt: '2026-10-11T12:00:00Z' });

    expect(service.activeIncidents()[0].status).toBe('ACKNOWLEDGED');
  });

  it('should call retryPayload API endpoint', () => {
    service.retryPayload('stripe-billing', 'evt-2').subscribe(res => {
      expect(res.status).toBe('RETRY_DISPATCHED');
      expect(res.targetResponseStatusCode).toBe(200);
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/incidents/stripe-billing/evt-2/retry`);
    expect(req.request.method).toBe('POST');

    req.flush({ status: 'RETRY_DISPATCHED', eventId: 'evt-2', targetResponseStatusCode: 200 });
  });
});

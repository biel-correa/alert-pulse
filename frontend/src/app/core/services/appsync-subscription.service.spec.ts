import { TestBed } from '@angular/core/testing';
import { AppSyncSubscriptionService } from './appsync-subscription.service';
import { WebhookEvent } from '../models';

describe('AppSyncSubscriptionService', () => {
  let service: AppSyncSubscriptionService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [AppSyncSubscriptionService]
    });
    service = TestBed.inject(AppSyncSubscriptionService);
  });

  afterEach(() => {
    service.disconnect();
  });

  it('should be created and have isConnected signal defined', () => {
    expect(service).toBeTruthy();
    expect(typeof service.isConnected()).toBe('boolean');
  });

  it('should dispatch events through subscribeToEvents when message is handled', (done) => {
    const mockEvent: WebhookEvent = {
      id: 'test-evt-1',
      endpointId: 'stripe-billing',
      timestamp: '2026-10-11T12:00:00Z',
      statusCode: 200,
      latencyMs: 140,
      severity: 'INFO',
      status: 'OPEN'
    };

    service.subscribeToEvents().subscribe(event => {
      expect(event.id).toBe('test-evt-1');
      expect(event.endpointId).toBe('stripe-billing');
      done();
    });

    // Invoke private handleMessage to simulate incoming WebSocket data
    (service as any).handleMessage({
      type: 'data',
      payload: {
        data: {
          onNewEvent: mockEvent
        }
      }
    });
  });

  it('should filter critical events through subscribeToCriticalIncidents', (done) => {
    const criticalEvent: WebhookEvent = {
      id: 'crit-evt-1',
      endpointId: 'stripe-billing',
      timestamp: '2026-10-11T12:00:00Z',
      statusCode: 500,
      latencyMs: 5200,
      severity: 'CRITICAL',
      status: 'OPEN',
      errorMessage: 'Database Connection Lost'
    };

    service.subscribeToCriticalIncidents().subscribe(event => {
      expect(event.id).toBe('crit-evt-1');
      expect(event.severity).toBe('CRITICAL');
      done();
    });

    // Send an INFO event (should not trigger critical subscriber)
    (service as any).handleMessage({
      type: 'data',
      payload: {
        data: {
          onNewEvent: { ...criticalEvent, id: 'info-1', severity: 'INFO' }
        }
      }
    });

    // Send CRITICAL event (triggers critical subscriber)
    (service as any).handleMessage({
      type: 'data',
      payload: {
        data: {
          onNewEvent: criticalEvent
        }
      }
    });
  });

  it('should set isConnected to false upon disconnect', () => {
    service.disconnect();
    expect(service.isConnected()).toBeFalse();
  });
});

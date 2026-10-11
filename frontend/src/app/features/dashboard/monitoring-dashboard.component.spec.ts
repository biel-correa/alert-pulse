import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { MonitoringDashboardComponent } from './monitoring-dashboard.component';
import { MonitoringService } from '../../core/services/monitoring.service';
import { AppSyncSubscriptionService } from '../../core/services/appsync-subscription.service';
import { WebhookEvent } from '../../core/models';

describe('MonitoringDashboardComponent', () => {
  let component: MonitoringDashboardComponent;
  let fixture: ComponentFixture<MonitoringDashboardComponent>;
  let monitorService: MonitoringService;
  let appSyncService: AppSyncSubscriptionService;

  const mockCriticalEvent: WebhookEvent = {
    id: 'evt-crit-1',
    endpointId: 'stripe-billing',
    timestamp: '2026-10-11T12:00:00Z',
    statusCode: 500,
    latencyMs: 3450,
    severity: 'CRITICAL',
    status: 'OPEN',
    errorMessage: 'Gateway Timeout'
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MonitoringDashboardComponent, HttpClientTestingModule],
      providers: [MonitoringService, AppSyncSubscriptionService]
    }).compileComponents();

    fixture = TestBed.createComponent(MonitoringDashboardComponent);
    component = fixture.componentInstance;
    monitorService = TestBed.inject(MonitoringService);
    appSyncService = TestBed.inject(AppSyncSubscriptionService);
    fixture.detectChanges();
  });

  it('should create the dashboard component', () => {
    expect(component).toBeTruthy();
  });

  it('should render empty state message when no events exist in buffer', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.empty-state')).toBeTruthy();
    expect(compiled.textContent).toContain('No webhook events recorded yet');
  });

  it('should render live incident rows with severity badges and control buttons', () => {
    monitorService.appendLiveEvent(mockCriticalEvent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.empty-state')).toBeFalsy();

    const row = compiled.querySelector('tr.row-critical');
    expect(row).toBeTruthy();
    expect(row?.textContent).toContain('stripe-billing');
    expect(row?.textContent).toContain('500');
    expect(row?.textContent).toContain('CRITICAL');
    expect(row?.textContent).toContain('Gateway Timeout');

    // Action buttons
    const ackBtn = row?.querySelector('.btn-action.ack');
    const retryBtn = row?.querySelector('.btn-action.retry');
    expect(ackBtn).toBeTruthy();
    expect(retryBtn).toBeTruthy();
  });

  it('should update metrics topbar values dynamically', () => {
    monitorService.appendLiveEvent(mockCriticalEvent);
    fixture.detectChanges();

    expect(monitorService.criticalCount()).toBe(1);
    expect(monitorService.openCount()).toBe(1);
    expect(monitorService.averageLatency()).toBe(3450);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.metric-card .value.critical')?.textContent).toContain('1');
  });

  it('should trigger simulateWebhook and display feedback', () => {
    spyOn(monitorService, 'simulateWebhook').and.returnValue(
      of({ eventId: 'sim-99', status: 'SIMULATED', severity: 'CRITICAL' })
    );

    component.triggerSimulation(true);

    expect(monitorService.simulateWebhook).toHaveBeenCalledWith('stripe-billing', true);
    expect(component.feedbackMessage()).toContain('sim-99');
    expect(component.feedbackType()).toBe('success');
  });

  it('should trigger acknowledgeIncident on ack button click', () => {
    spyOn(monitorService, 'acknowledgeIncident').and.returnValue(
      of({ eventId: 'evt-crit-1', status: 'ACKNOWLEDGED', acknowledgedAt: '2026-10-11T12:00:00Z' })
    );

    component.ack(mockCriticalEvent);

    expect(monitorService.acknowledgeIncident).toHaveBeenCalledWith('stripe-billing', 'evt-crit-1');
    expect(component.feedbackMessage()).toContain('acknowledged');
  });

  it('should trigger retryPayload on retry button click', () => {
    spyOn(monitorService, 'retryPayload').and.returnValue(
      of({ status: 'RETRY_DISPATCHED', eventId: 'evt-crit-1', targetResponseStatusCode: 200 })
    );

    component.retry(mockCriticalEvent);

    expect(monitorService.retryPayload).toHaveBeenCalledWith('stripe-billing', 'evt-crit-1');
    expect(component.feedbackMessage()).toContain('replayed');
  });

  it('should handle retry error gracefully', () => {
    spyOn(monitorService, 'retryPayload').and.returnValue(
      throwError(() => new Error('Connection refused'))
    );

    component.retry(mockCriticalEvent);

    expect(component.feedbackType()).toBe('error');
    expect(component.feedbackMessage()).toContain('Connection refused');
  });
});

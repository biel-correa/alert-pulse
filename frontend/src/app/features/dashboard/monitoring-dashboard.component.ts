import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MonitoringService } from '../../core/services/monitoring.service';
import { AppSyncSubscriptionService } from '../../core/services/appsync-subscription.service';
import { WebhookEvent } from '../../core/models';

@Component({
  selector: 'app-monitoring-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './monitoring-dashboard.component.html',
  styleUrls: ['./monitoring-dashboard.component.scss']
})
export class MonitoringDashboardComponent implements OnInit {
  protected readonly monitor = inject(MonitoringService);
  protected readonly appSync = inject(AppSyncSubscriptionService);

  readonly isSimulating = signal<boolean>(false);
  readonly feedbackMessage = signal<string | null>(null);
  readonly feedbackType = signal<'success' | 'error' | 'info'>('info');

  ngOnInit(): void {
    // 1. Subscribe to live AppSync events over WebSocket
    this.appSync.subscribeToEvents().subscribe({
      next: (event: WebhookEvent) => {
        this.monitor.appendLiveEvent(event);
      },
      error: (err) => console.warn('[Dashboard] Subscription stream error:', err)
    });
  }

  // --- Simulation Triggers ---

  triggerSimulation(asError: boolean): void {
    this.isSimulating.set(true);
    this.monitor.simulateWebhook('stripe-billing', asError).subscribe({
      next: (res) => {
        this.isSimulating.set(false);
        this.showFeedback(
          `Simulated ${res.severity} event successfully dispatched (ID: ${res.eventId}).`,
          'success'
        );
      },
      error: (err) => {
        this.isSimulating.set(false);
        this.showFeedback(`Simulation failed: ${err.message || 'Unknown error'}`, 'error');
      }
    });
  }

  // --- Mitigation Actions ---

  ack(item: WebhookEvent): void {
    this.monitor.acknowledgeIncident(item.endpointId, item.id).subscribe({
      next: () => {
        this.showFeedback(`Incident ${item.id} acknowledged.`, 'info');
      },
      error: (err) => {
        this.showFeedback(`Failed to acknowledge incident: ${err.message}`, 'error');
      }
    });
  }

  retry(item: WebhookEvent): void {
    this.monitor.retryPayload(item.endpointId, item.id).subscribe({
      next: (res) => {
        this.showFeedback(
          `Payload replayed for incident ${item.id} (Status Code: ${res.targetResponseStatusCode}).`,
          'success'
        );
      },
      error: (err) => {
        this.showFeedback(`Payload replay failed: ${err.message}`, 'error');
      }
    });
  }

  // --- WebSocket Diagnostics ---

  reconnectWebSocket(): void {
    this.appSync.reconnect();
    this.showFeedback('Reconnection signal sent to AppSync WebSocket client.', 'info');
  }

  dismissFeedback(): void {
    this.feedbackMessage.set(null);
  }

  private showFeedback(message: string, type: 'success' | 'error' | 'info' = 'info'): void {
    this.feedbackMessage.set(message);
    this.feedbackType.set(type);

    // Auto-dismiss after 6 seconds
    setTimeout(() => {
      if (this.feedbackMessage() === message) {
        this.feedbackMessage.set(null);
      }
    }, 6000);
  }
}

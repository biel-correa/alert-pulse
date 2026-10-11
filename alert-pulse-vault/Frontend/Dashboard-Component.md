---
title: "Operational Dashboard Component"
tags:
  - frontend
  - angular
  - component
  - ui
  - dashboard
date: 2026-10-11
status: approved
---

# Operational Dashboard Component

The `MonitoringDashboardComponent` is the central control room where NOC operators and SRE engineers track webhook health, observe live incidents, and execute mitigation actions.

---

## 💻 Source Code: `monitoring-dashboard.component.ts`

```typescript
import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MonitoringService, WebhookEvent } from '../../core/services/monitoring.service';
import { AppSyncSubscriptionService } from '../../core/services/appsync-subscription.service';

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

  ngOnInit(): void {
    // Subscribe to live AppSync events over WebSocket
    this.appSync.subscribeToEvents().subscribe({
      next: (event: WebhookEvent) => {
        this.monitor.appendLiveEvent(event);
      },
      error: (err) => console.error('AppSync subscription error:', err)
    });
  }

  // --- Simulation Triggers ---

  triggerSimulation(asError: boolean): void {
    this.monitor.simulateWebhook('stripe-billing', asError).subscribe();
  }

  // --- Mitigation Actions ---

  ack(item: WebhookEvent): void {
    this.monitor.acknowledgeIncident(item.endpointId, item.id).subscribe();
  }

  retry(item: WebhookEvent): void {
    this.monitor.retryPayload(item.endpointId, item.id).subscribe({
      next: () => alert(`Payload successfully re-dispatched for event ${item.id}!`),
      error: (err) => alert(`Retry failed: ${err.message}`)
    });
  }
}
```

---

## 📄 HTML Template (Angular 22 Control Flow)

```html
<!-- monitoring-dashboard.component.html -->
<div class="dashboard-container">
  <!-- Topbar with Metrics and Actions -->
  <header class="dashboard-header">
    <div class="brand">
      <h1>Alert Pulse <span>Live Incident Hub</span></h1>
      <div class="connection-status" [class.connected]="appSync.isConnected()">
        <span class="status-dot"></span>
        {{ appSync.isConnected() ? 'WebSocket Connected' : 'Reconnecting...' }}
      </div>
    </div>

    <!-- Real-Time Metrics (Computed Signals) -->
    <div class="metrics-bar">
      <div class="metric-card">
        <span class="label">Critical Incidents</span>
        <span class="value critical">{{ monitor.criticalCount() }}</span>
      </div>
      <div class="metric-card">
        <span class="label">Open Incidents</span>
        <span class="value">{{ monitor.openCount() }}</span>
      </div>
      <div class="metric-card">
        <span class="label">Average Latency</span>
        <span class="value">{{ monitor.averageLatency() }}ms</span>
      </div>
    </div>

    <!-- Pipeline Testing Actions -->
    <div class="simulation-actions">
      <button (click)="triggerSimulation(false)" class="btn btn-secondary">
        ⚡ Test OK (200)
      </button>
      <button (click)="triggerSimulation(true)" class="btn btn-danger">
        🔥 Simulate Critical (500)
      </button>
    </div>
  </header>

  <!-- Live Incident Stream Table -->
  <main class="table-container">
    <table>
      <thead>
        <tr>
          <th>Status</th>
          <th>Endpoint</th>
          <th>HTTP Code</th>
          <th>Latency</th>
          <th>Severity</th>
          <th>Error Message</th>
          <th>Actions</th>
        </tr>
      </thead>
      <tbody>
        @for (item of monitor.activeIncidents(); track item.id) {
          <tr [class]="'row-' + item.severity.toLowerCase()">
            <td>
              <span class="status-tag" [class]="item.status.toLowerCase()">
                {{ item.status }}
              </span>
            </td>
            <td class="endpoint-cell"><code>{{ item.endpointId }}</code></td>
            <td>
              <strong [class.text-danger]="item.statusCode >= 500">
                {{ item.statusCode }}
              </strong>
            </td>
            <td>{{ item.latencyMs }}ms</td>
            <td>
              <span class="badge" [class]="'badge-' + item.severity.toLowerCase()">
                {{ item.severity }}
              </span>
            </td>
            <td class="error-cell">
              {{ item.errorMessage || '—' }}
            </td>
            <td class="actions-cell">
              @if (item.status === 'OPEN') {
                <button (click)="ack(item)" class="btn-action ack">
                  Acknowledge
                </button>
              }
              @if (item.statusCode >= 400) {
                <button (click)="retry(item)" class="btn-action retry">
                  Retry
                </button>
              }
            </td>
          </tr>
        } @empty {
          <tr>
            <td colspan="7" class="empty-state">
              No events recorded yet. Awaiting webhooks or trigger a simulation above.
            </td>
          </tr>
        }
      </tbody>
    </table>
  </main>
</div>
```

---

## 🎨 SCSS Styling (Operational SRE/NOC Dark Theme)

```scss
/* monitoring-dashboard.component.scss */
.dashboard-container {
  background-color: #0f172a;
  color: #f8fafc;
  min-height: 100vh;
  padding: 1.5rem;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}

.dashboard-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: #1e293b;
  padding: 1.25rem 2rem;
  border-radius: 12px;
  margin-bottom: 1.5rem;
  border: 1px solid #334155;

  .brand h1 {
    font-size: 1.5rem;
    font-weight: 700;
    margin: 0;
    span { font-size: 0.9rem; color: #94a3b8; font-weight: 400; margin-left: 0.5rem; }
  }

  .connection-status {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    font-size: 0.8rem;
    color: #f59e0b;
    margin-top: 0.25rem;

    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background-color: #f59e0b;
    }

    &.connected {
      color: #10b981;
      .status-dot { background-color: #10b981; box-shadow: 0 0 8px #10b981; }
    }
  }
}

.metrics-bar {
  display: flex;
  gap: 1.5rem;

  .metric-card {
    background-color: #0f172a;
    padding: 0.75rem 1.25rem;
    border-radius: 8px;
    border: 1px solid #334155;
    display: flex;
    flex-direction: column;

    .label { font-size: 0.75rem; color: #94a3b8; text-transform: uppercase; }
    .value { font-size: 1.5rem; font-weight: 700; color: #f8fafc; }
    .value.critical { color: #ef4444; }
  }
}

.table-container {
  background-color: #1e293b;
  border-radius: 12px;
  overflow: hidden;
  border: 1px solid #334155;

  table {
    width: 100%;
    border-collapse: collapse;
    text-align: left;

    th {
      background-color: #0f172a;
      padding: 1rem;
      font-size: 0.85rem;
      color: #94a3b8;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    td {
      padding: 1rem;
      border-bottom: 1px solid #334155;
      font-size: 0.9rem;
    }
  }
}

.badge {
  padding: 0.25rem 0.6rem;
  border-radius: 9999px;
  font-size: 0.75rem;
  font-weight: 700;

  &.badge-critical { background-color: rgba(239, 68, 68, 0.2); color: #ef4444; border: 1px solid #ef4444; }
  &.badge-warning { background-color: rgba(245, 158, 11, 0.2); color: #f59e0b; border: 1px solid #f59e0b; }
  &.badge-info { background-color: rgba(59, 130, 246, 0.2); color: #60a5fa; border: 1px solid #3b82f6; }
}

.btn-action {
  padding: 0.4rem 0.8rem;
  border-radius: 6px;
  border: none;
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 600;
  margin-right: 0.5rem;

  &.ack { background-color: #3b82f6; color: #ffffff; &:hover { background-color: #2563eb; } }
  &.retry { background-color: #f59e0b; color: #000; &:hover { background-color: #d97706; } }
}
```

---

## 🔗 Related Notes
* Services powering this component: [[Monitoring-Service|Telemetry & State Service]] and [[Realtime-Subscriptions|WebSocket Subscriptions]].
* Testing procedures: [[Testing-Strategy|Testing Strategy]].

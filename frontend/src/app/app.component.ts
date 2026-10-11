import { Component } from '@angular/core';
import { MonitoringDashboardComponent } from './features/dashboard/monitoring-dashboard.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [MonitoringDashboardComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  title = 'Alert Pulse';
}

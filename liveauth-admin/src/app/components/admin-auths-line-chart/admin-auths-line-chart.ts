import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration } from 'chart.js';

@Component({
  selector: 'admin-auths-line-chart',
  standalone: true,
  imports: [CommonModule, BaseChartDirective],
  template: `
    <div class="chart-card">
      <h4>Auths Over Time</h4>
      <canvas
        baseChart
        [data]="data"
        [options]="options"
        chartType="line">
      </canvas>
    </div>
  `
})
export class AdminAuthsLineChartComponent {
  data: ChartConfiguration<'line'>['data'] = {
    labels: [],
    datasets: [
      {
        label: 'Successful',
        data: [],
        borderColor: '#4ade80',
        tension: 0.3
      },
      {
        label: 'Failed',
        data: [],
        borderColor: '#f87171',
        tension: 0.3
      }
    ]
  };

  options: ChartConfiguration<'line'>['options'] = {
    responsive: true,
    plugins: { legend: { position: 'bottom' } }
  };

  @Input() set series(value: any[]) {
    // Always replace the entire data object to ensure chart updates when inputs change
    if (!value || value.length === 0) {
      this.data = {
        labels: [],
        datasets: [
          { label: 'Successful', data: [], borderColor: '#4ade80', tension: 0.3 },
          { label: 'Failed', data: [], borderColor: '#f87171', tension: 0.3 }
        ]
      };
      return;
    }

    const labels = value.map(v => {
      const d = new Date(v.timestampUtc);
      // Show day and hour for broader ranges to avoid collisions
      return d.toLocaleString([], { month: 'short', day: '2-digit', hour: '2-digit' });
    });

    const successful = value.map(v => v.successful);
    const failed = value.map(v => v.failed);

    this.data = {
      labels,
      datasets: [
        { label: 'Successful', data: successful, borderColor: '#4ade80', tension: 0.3 },
        { label: 'Failed', data: failed, borderColor: '#f87171', tension: 0.3 }
      ]
    };
  }
}

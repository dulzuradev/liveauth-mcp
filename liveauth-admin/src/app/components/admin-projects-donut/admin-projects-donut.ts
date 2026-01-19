import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';

@Component({
  selector: 'admin-projects-donut',
  standalone: true,
  imports: [
    CommonModule,
    BaseChartDirective
  ],
  template: `
    <div class="chart-card">
      <h4>Projects</h4>
      <canvas
        baseChart
        [data]="data"
        [type]="'doughnut'">
      </canvas>
    </div>
  `
})
export class AdminProjectsDonutComponent {
  data = {
    labels: ['Pro', 'Free'],
    datasets: [
      {
        data: [0, 0],
        backgroundColor: ['#4ade80', '#60a5fa']
      }
    ]
  };

  @Input() set proFree(value: { pro: number; free: number } | null) {
    if (!value) {
      this.data = {
        labels: ['Pro', 'Free'],
        datasets: [
          { data: [0, 0], backgroundColor: ['#4ade80', '#60a5fa'] }
        ]
      };
      return;
    }

    this.data = {
      labels: ['Pro', 'Free'],
      datasets: [
        { data: [value.pro, value.free], backgroundColor: ['#4ade80', '#60a5fa'] }
      ]
    };
  }
}

import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartData } from 'chart.js';

@Component({
  selector: 'admin-projects-donut',
  standalone: true,
  imports: [CommonModule, BaseChartDirective],
  templateUrl: './admin-projects-donut.html',
  styleUrls: ['./admin-projects-donut.css']
})
export class AdminProjectsDonutComponent implements OnChanges {
  proCount = 0;
  freeCount = 0;

  data: ChartData<'doughnut'> = {
    labels: ['Pro', 'Free'],
    datasets: [{
      data: [0, 0],
      backgroundColor: ['#f59e0b', 'rgba(255,255,255,0.24)'],
      borderColor: '#151719',
      borderWidth: 3,
      hoverBorderWidth: 0,
      hoverOffset: 6
    }]
  };

  donutOptions: ChartConfiguration<'doughnut'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    cutout: '68%',
    plugins: {
      legend: { display: false },
      tooltip: {
        backgroundColor: '#151719',
        titleColor: '#f7f9fb',
        bodyColor: 'rgba(255,255,255,0.64)',
        borderColor: 'rgba(245,158,11,0.34)',
        borderWidth: 1,
        padding: 10,
        cornerRadius: 8,
        callbacks: {
          label: (ctx) => ` ${ctx.label}: ${ctx.raw}`
        }
      }
    }
  };

  @Input() set proFree(value: { pro: number; free: number } | null) {
    const pro = value?.pro ?? 0;
    const free = value?.free ?? 0;
    this.proCount = pro;
    this.freeCount = free;
    this.data = {
      ...this.data,
      datasets: [{
        ...this.data.datasets[0],
        data: [pro, free]
      }]
    };
  }

  ngOnChanges() {}
}

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
      backgroundColor: ['#4ade80', '#3b82f6'],
      borderColor: 'rgba(17,24,45,0.8)',
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
        backgroundColor: 'rgba(10,15,30,0.95)',
        titleColor: '#e3e7ee',
        bodyColor: '#8b95a5',
        borderColor: 'rgba(0,194,255,0.2)',
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
import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartData } from 'chart.js';

@Component({
  selector: 'admin-auths-line-chart',
  standalone: true,
  imports: [CommonModule, BaseChartDirective],
  templateUrl: './admin-auths-line-chart.html',
  styleUrls: ['./admin-auths-line-chart.css']
})
export class AdminAuthsLineChartComponent implements OnChanges {
  data: ChartData<'line'> = {
    labels: [],
    datasets: [
      {
        label: 'Successful',
        data: [],
        borderColor: '#4ade80',
        backgroundColor: 'rgba(74,222,128,0.08)',
        fill: true,
        tension: 0.4,
        pointRadius: 3,
        pointHoverRadius: 5,
        pointBackgroundColor: '#4ade80'
      },
      {
        label: 'Failed',
        data: [],
        borderColor: '#f87171',
        backgroundColor: 'rgba(248,113,113,0.06)',
        fill: true,
        tension: 0.4,
        pointRadius: 3,
        pointHoverRadius: 5,
        pointBackgroundColor: '#f87171'
      }
    ]
  };

  options: ChartConfiguration<'line'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    interaction: { mode: 'index', intersect: false },
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
        displayColors: true,
        boxWidth: 10,
        boxHeight: 10
      }
    },
    scales: {
      x: {
        grid: { color: 'rgba(255,255,255,0.04)' },
        ticks: { color: '#8b95a5', maxTicksLimit: 8, font: { size: 11 } },
        border: { display: false }
      },
      y: {
        grid: { color: 'rgba(255,255,255,0.04)' },
        ticks: { color: '#8b95a5', font: { size: 11 }, maxTicksLimit: 6 },
        border: { display: false },
        beginAtZero: true
      }
    }
  };

  private _series: any[] = [];

  @Input() set series(value: any[]) {
    this._series = value ?? [];
    this.updateChart();
  }

  ngOnChanges() {
    this.updateChart();
  }

  private updateChart() {
    if (this._series.length === 0) {
      this.data = { ...this.data, labels: [], datasets: this.buildDatasets([]) };
      return;
    }

    const labels = this._series.map(v => {
      const d = new Date(v.timestampUtc);
      return d.toLocaleString([], { month: 'short', day: '2-digit', hour: '2-digit' });
    });

    this.data = {
      ...this.data,
      labels,
      datasets: this.buildDatasets(this._series)
    };
  }

  private buildDatasets(series: any[]): any {
    return [
      {
        ...this.data.datasets[0],
        data: series.map(v => v.successful ?? 0)
      },
      {
        ...this.data.datasets[1],
        data: series.map(v => v.failed ?? 0)
      }
    ];
  }
}
import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AdminLoginComponent } from './components/admin-login-component/admin-login-component';
import { Chart, registerables } from 'chart.js';

Chart.register(...registerables);

// LiveAuth dark console Chart.js defaults.
Chart.defaults.color = 'rgba(255,255,255,0.64)';
Chart.defaults.borderColor = 'rgba(255,255,255,0.1)';
Chart.defaults.backgroundColor = '#151719';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('liveauth-admin');
}

import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AdminLoginComponent } from './components/admin-login-component/admin-login-component';
import { Chart, registerables } from 'chart.js';

Chart.register(...registerables);

// Dark-mode Chart.js defaults
Chart.defaults.color = '#8b95a5';
Chart.defaults.borderColor = 'rgba(255,255,255,0.08)';
Chart.defaults.backgroundColor = 'rgba(17,24,45,0.8)';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('liveauth-admin');
}

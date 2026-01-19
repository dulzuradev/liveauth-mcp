import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import {HTTP_INTERCEPTORS, provideHttpClient, withInterceptorsFromDi} from '@angular/common/http';
import {AdminAuthInterceptor} from './app/interceptors/admin-auth.interceptor';
import {provideBrowserGlobalErrorListeners} from '@angular/core';
import {provideRouter} from '@angular/router';
import {routes} from './app/app.routes';
import { provideCharts } from 'ng2-charts';
import {
  Chart,
  BarController,
  BarElement,
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  DoughnutController,
  ArcElement
} from 'chart.js';
import {providePrimeNG} from 'primeng/config';
// import LaraDarkBlue from '@primeuix/themes/lara-dark-blue';
import LaraDarkBlue from '@primeuix/themes/lara';

Chart.register(
  BarController,
  BarElement,
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  DoughnutController,
  ArcElement
);

bootstrapApplication(App, {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptorsFromDi()),
    provideRouter(routes),
    {
      provide: HTTP_INTERCEPTORS,
      useClass: AdminAuthInterceptor,
      multi: true
    },
    provideCharts(),
    providePrimeNG({
      theme: {
        preset: LaraDarkBlue,
        options: {
          cssLayer: false
        }
      }
    })
  ]})
  .catch((err) => console.error(err));

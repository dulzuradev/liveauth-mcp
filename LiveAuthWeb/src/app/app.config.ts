import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';

import { routes } from './app.routes';
import {providePrimeNG} from 'primeng/config';
import Lara from '@primeuix/themes/lara';
import { ClipboardModule } from '@angular/cdk/clipboard';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(),
    provideAnimations(),
    providePrimeNG({
      theme: {
        preset: Lara,          // same family as lara-light-blue
        options: {
          darkModeSelector: '.dark' // optional if you want dark mode toggle
        }
      }
    })
  ]
};

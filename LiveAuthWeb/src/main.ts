import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

// Use the shared application configuration which includes the router
// and other global providers.
bootstrapApplication(App, appConfig).catch((err) => console.error(err));

import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import {HTTP_INTERCEPTORS, provideHttpClient, withInterceptors} from '@angular/common/http';
import {AdminAuthInterceptor} from './interceptors/admin-auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    {
      provide: HTTP_INTERCEPTORS,
      useClass: AdminAuthInterceptor,
      multi: true
    }
    // provideHttpClient(
    //   withInterceptors([
    //     (req, next) => {
    //       if (!req.url.includes('/api/admin')) {
    //         return next(req);
    //       }
    //
    //       const token = localStorage.getItem('admin_token');
    //       if (!token) {
    //         return next(req);
    //       }
    //
    //       return next(
    //         req.clone({
    //           setHeaders: {
    //             Authorization: `Bearer ${token}`
    //           }
    //         })
    //       );
    //     }
    //   ])
    // )
  ]
};

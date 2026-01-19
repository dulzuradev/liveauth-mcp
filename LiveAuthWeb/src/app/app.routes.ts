import { Routes } from '@angular/router';
import { LoginComponent } from './pages/demo/login-component/login-component';
import { MockLoginComponent } from './pages/demo/mock-login/mock-login.component';
import {DeveloperProjectsComponent} from './pages/dev/developer-projects/developer-projects';
import {LandingPageComponent} from './pages/landing-page/landing-page';
import {AdminComponent} from './pages/dev/admin-component/admin-component';

export const routes: Routes = [
  // Landing Page (new)
  { path: '', component: LandingPageComponent },

  // Lightning Wall demo
  { path: 'demo', component: LoginComponent },

  // Developer console
  { path: 'dev/projects', component: DeveloperProjectsComponent },

  // Admin (optional internal)
  { path: 'admin', component: AdminComponent },

  // Mock login (used after LN payment)
  { path: 'mock-login', component: MockLoginComponent },

  // Wildcard fallback
  { path: '**', redirectTo: '' }
];

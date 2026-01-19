import {Routes} from '@angular/router';
import {AdminLoginComponent} from './components/admin-login-component/admin-login-component';
import {AdminDashboardComponent} from './components/admin-dashboard-component/admin-dashboard-component';
import {AdminAuthGuard} from './guards/admin-auth.guard';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'login',
    pathMatch: 'full'
  },
  {
    path: 'login',
    component: AdminLoginComponent
  },
  // {
  //   path: '',
  //   component: AdminDashboardComponent,
  //   canActivate: [AdminAuthGuard]
  // },
  {
    path: 'admin',
    component: AdminDashboardComponent,
    canActivate: [AdminAuthGuard]
  },
  {
    path: '**',
    redirectTo: 'login'
  }
];

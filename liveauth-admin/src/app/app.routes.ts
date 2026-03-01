import {Routes} from '@angular/router';
import {AdminLoginComponent} from './components/admin-login-component/admin-login-component';
import {AdminDashboardComponent} from './components/admin-dashboard-component/admin-dashboard-component';
import {AdminTransactionsComponent} from './components/admin-transactions/admin-transactions';
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
  {
    path: 'admin',
    component: AdminDashboardComponent,
    canActivate: [AdminAuthGuard]
  },
  {
    path: 'transactions',
    component: AdminTransactionsComponent,
    canActivate: [AdminAuthGuard]
  },
  {
    path: '**',
    redirectTo: 'login'
  }
];

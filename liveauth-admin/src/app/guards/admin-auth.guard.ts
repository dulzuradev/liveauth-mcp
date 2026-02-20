import {inject, Injectable} from '@angular/core';
import {CanActivate, Router} from '@angular/router';
import { AdminAuthService } from '../services/admin-auth';

@Injectable({ providedIn: 'root' })
export class AdminAuthGuard implements CanActivate {
  constructor(
    private auth: AdminAuthService,
    private router: Router
  ) {}

  canActivate(): boolean {
    if (this.auth.isLoggedIn()) {
      return true;
    }

    this.router.navigate(['/login']);
    return false;
  }
}

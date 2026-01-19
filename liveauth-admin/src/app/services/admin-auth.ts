import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

export interface AdminStartLoginResponse {
  sessionId: string;
  invoice: string;
  amountSats: number;
  expiresAtUnix: number;
}

export interface AdminConfirmLoginResponse {
  verified: boolean;
  token?: string | null;
  expiresAtUnix?: number | null;
}

@Injectable({ providedIn: 'root' })
export class AdminAuthService {
  private baseUrl = 'http://localhost:5166';
  private tokenKey = 'liveauth_admin_jwt';

  constructor(private http: HttpClient) {}

  startLogin(email: string): Observable<AdminStartLoginResponse> {
    return this.http.post<AdminStartLoginResponse>(
      `${this.baseUrl}/api/admin/auth/start`,
      { email }
    );
  }

  confirmLogin(sessionId: string): Observable<AdminConfirmLoginResponse> {
    return this.http.post<AdminConfirmLoginResponse>(
      `${this.baseUrl}/api/admin/auth/confirm`,
      { sessionId }
    );
  }

  saveToken(token: string) {
    sessionStorage.setItem(this.tokenKey, token);
  }

  getToken(): string | null {
    return sessionStorage.getItem(this.tokenKey);
  }

  clearToken() {
    sessionStorage.removeItem(this.tokenKey);
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
  }

  hasValidToken(): boolean {
    const token = this.getToken();
    if (!token) return false;

    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload?.exp;
      return typeof exp === 'number' && exp * 1000 > Date.now();
    } catch {
      return false;
    }
  }

}

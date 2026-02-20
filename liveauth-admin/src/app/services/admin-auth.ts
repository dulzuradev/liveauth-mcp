import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, BehaviorSubject } from 'rxjs';

const API_URL = 'https://api.liveauth.app';

export interface AdminStatusResponse {
  isAuthenticated: boolean;
  username?: string;
  isOwner?: boolean;
}

export interface AdminPaymentResponse {
  sessionId: string;
  invoice: string;
  amountSats: number;
  isSetup: boolean;
  expiresAtUnix: number;
}

export interface AdminVerifyResponse {
  paid: boolean;
  canSetPassword?: boolean;
  error?: string;
}

export interface AdminSetupRequest {
  username: string;
  password: string;
}

export interface AdminSetupResponse {
  success: boolean;
  token: string;
  username: string;
}

export interface AdminLoginRequest {
  username: string;
  password: string;
}

export interface AdminLoginResponse {
  success: boolean;
  token?: string;
  username?: string;
  error?: string;
}

@Injectable({ providedIn: 'root' })
export class AdminAuthService {
  private tokenKey = 'liveauth_admin_token';
  private usernameKey = 'liveauth_admin_username';
  
  private authState$ = new BehaviorSubject<AdminStatusResponse>({ isAuthenticated: false });

  constructor(private http: HttpClient) {
    this.checkStatus();
  }

  checkStatus(): Observable<AdminStatusResponse> {
    const token = this.getToken();
    if (!token) {
      this.authState$.next({ isAuthenticated: false });
      return new Observable(sub => sub.next({ isAuthenticated: false }));
    }
    
    return this.http.get<AdminStatusResponse>(`${API_URL}/api/admin/auth/status`, {
      headers: { Authorization: `Bearer ${token}` }
    }).pipe(
      tap(res => this.authState$.next(res))
    );
  }

  getAuthState() {
    return this.authState$.asObservable();
  }

  createPayment(): Observable<AdminPaymentResponse> {
    return this.http.post<AdminPaymentResponse>(`${API_URL}/api/admin/auth/payment`, {});
  }

  verifyPayment(sessionId: string): Observable<AdminVerifyResponse> {
    return this.http.post<AdminVerifyResponse>(`${API_URL}/api/admin/auth/verify`, { sessionId });
  }

  setupAdmin(username: string, password: string): Observable<AdminSetupResponse> {
    return this.http.post<AdminSetupResponse>(`${API_URL}/api/admin/auth/setup`, { username, password });
  }

  login(username: string, password: string): Observable<AdminLoginResponse> {
    return this.http.post<AdminLoginResponse>(`${API_URL}/api/admin/auth/login`, { username, password }).pipe(
      tap(res => {
        if (res.success && res.token) {
          this.saveToken(res.token);
          this.saveUsername(res.username || '');
          this.authState$.next({ isAuthenticated: true, username: res.username, isOwner: true });
        }
      })
    );
  }

  logout(): Observable<any> {
    return this.http.post(`${API_URL}/api/admin/auth/logout`, {}).pipe(
      tap(() => {
        this.clearToken();
        this.authState$.next({ isAuthenticated: false });
      })
    );
  }

  saveToken(token: string) {
    localStorage.setItem(this.tokenKey, token);
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  saveUsername(username: string) {
    localStorage.setItem(this.usernameKey, username);
  }

  getUsername(): string | null {
    return localStorage.getItem(this.usernameKey);
  }

  clearToken() {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.usernameKey);
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
  }
}

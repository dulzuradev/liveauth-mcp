import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BASE_API_URL } from '../config';

export interface DevStartLoginRequest {
  developerEmail: string;
}

export interface DevStartLoginResponse {
  sessionId: string;
  invoice: string;
  amountSats: number;
  expiresAtUnix: number;
}

export interface DevConfirmLoginRequest {
  sessionId: string;
}

export interface DevConfirmLoginResponse {
  verified: boolean;
  token?: string | null;
}

export interface GitHubLoginStatusResponse {
  enabled: boolean;
}

export interface GitHubLoginResponse {
  token: string;
  developer: {
    id: string;
    email: string;
    githubUsername?: string;
  };
}

@Injectable({ providedIn: 'root' })
export class DevAuthService {
  private baseUrl = BASE_API_URL;

  constructor(private http: HttpClient) {}

  // POST /api/dev/auth/start
  startLogin(req: DevStartLoginRequest): Observable<DevStartLoginResponse> {
    return this.http.post<DevStartLoginResponse>(
      `${this.baseUrl}/api/dev/auth/start`,
      req
    );
  }

  // POST /api/dev/auth/confirm
  confirmLogin(req: DevConfirmLoginRequest): Observable<DevConfirmLoginResponse> {
    return this.http.post<DevConfirmLoginResponse>(
      `${this.baseUrl}/api/dev/auth/confirm`,
      req
    );
  }

  // GET /api/dev/auth/github/status
  getGitHubStatus(): Observable<GitHubLoginStatusResponse> {
    return this.http.get<GitHubLoginStatusResponse>(
      `${this.baseUrl}/api/dev/auth/github/status`
    );
  }

  // GET /api/dev/auth/github/start - redirects to GitHub
  startGitHubLogin(): void {
    // Pass debug=true to force skip-GitHub flow on the API side
    // Also pass returnUrl so we land on the right page after login
    const returnUrl = encodeURIComponent(window.location.origin + '/dev/projects');
    window.location.href = `${this.baseUrl}/api/dev/auth/github/start?debug=true&returnUrl=${returnUrl}`;
  }

  saveToken(token: string) {
    localStorage.setItem('liveauth_dev_jwt', token);
  }

  getToken(): string | null {
    return localStorage.getItem('liveauth_dev_jwt');
  }

  clearToken() {
    localStorage.removeItem('liveauth_dev_jwt');
  }

  authHeaders(): { headers: HttpHeaders } {
    const token = this.getToken();
    return {
      headers: new HttpHeaders({
        Authorization: token ? `Bearer ${token}` : ''
      })
    };
  }
}

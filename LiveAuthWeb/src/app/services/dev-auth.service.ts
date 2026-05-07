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

export interface RegisterRequest {
  email: string;
  password: string;
}

export interface RegisterResponse {
  developerId: string;
  message: string;
  emailVerificationRequired: boolean;
  emailSent: boolean;
}

export interface VerifyEmailRequest {
  token: string;
}

export interface VerifyEmailResponse {
  success: boolean;
  token?: string | null;
  message: string;
}

export interface EmailLoginRequest {
  email: string;
  password: string;
}

export interface EmailLoginResponse {
  verified: boolean;
  token?: string | null;
  message: string;
}

export interface ForgotPasswordRequest {
  email: string;
}

export interface ForgotPasswordResponse {
  emailSent: boolean;
  message: string;
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

  // GET /api/dev/auth/github/start - redirects to GitHub, or returns { redirectUrl } in dev mode
  startGitHubLogin(devBypass = false): void {
    const url = `${this.baseUrl}/api/dev/auth/github/start${devBypass ? '?dev=true' : ''}`;
    
    // For dev bypass, we get JSON back instead of a redirect
    if (devBypass) {
      this.http.get<{ redirectUrl: string }>(url).subscribe({
        next: (res) => {
          window.location.href = res.redirectUrl;
        },
        error: (err) => {
          console.error('Dev login failed:', err);
          // Fall back to regular GitHub flow
          window.location.href = url;
        }
      });
    } else {
      window.location.href = url;
    }
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

  getApiUrl(): string {
    return this.baseUrl;
  }

  // POST /api/dev/auth/register
  register(req: RegisterRequest): Observable<RegisterResponse> {
    return this.http.post<RegisterResponse>(
      `${this.baseUrl}/api/dev/auth/register`,
      req
    );
  }

  // POST /api/dev/auth/verify-email
  verifyEmail(req: VerifyEmailRequest): Observable<VerifyEmailResponse> {
    return this.http.post<VerifyEmailResponse>(
      `${this.baseUrl}/api/dev/auth/verify-email`,
      req
    );
  }

  // POST /api/dev/auth/login (email/password)
  emailLogin(req: EmailLoginRequest): Observable<EmailLoginResponse> {
    return this.http.post<EmailLoginResponse>(
      `${this.baseUrl}/api/dev/auth/login`,
      req
    );
  }

  // POST /api/dev/auth/forgot-password
  forgotPassword(req: ForgotPasswordRequest): Observable<ForgotPasswordResponse> {
    return this.http.post<ForgotPasswordResponse>(
      `${this.baseUrl}/api/dev/auth/forgot-password`,
      req
    );
  }

  // POST /api/dev/auth/resend-verification
  resendVerification(req: { email: string }): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(
      `${this.baseUrl}/api/dev/auth/resend-verification`,
      req
    );
  }
}

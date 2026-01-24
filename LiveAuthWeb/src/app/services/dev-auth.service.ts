import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BASE_API_URL } from '../config';

export interface DevStartLoginRequest {
  developerEmail: string;
  // amountSats?: number; // backend currently ignores this; keep if you want future override
}

export interface DevStartLoginResponse {
  sessionId: string;
  invoice: string;        // BOLT11
  amountSats: number;
  expiresAtUnix: number;  // unix timestamp (seconds)
  // no developerId / paymentHash in current backend response
}

export interface DevConfirmLoginRequest {
  sessionId: string;
}

export interface DevConfirmLoginResponse {
  verified: boolean;
  token?: string | null;
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

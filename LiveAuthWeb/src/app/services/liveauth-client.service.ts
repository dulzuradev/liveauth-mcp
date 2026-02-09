import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import {
  catchError,
  from,
  map,
  Observable,
  of,
  switchMap,
  throwError,
  timeout,
} from 'rxjs';
import { BASE_API_URL } from '../config';

/* ------------------------------------------------------------------ */
/* POW TYPES (MATCHES YOUR /pow/challenge RESPONSE)                    */
/* ------------------------------------------------------------------ */

export interface PowChallengeResponse {
  projectPublicKey: string;
  challengeHex: string;
  targetHex: string;
  difficultyBits: number;
  expiresAtUnix: number;
  sig: string;
}

export interface PowVerifyRequest {
  challengeHex: string;
  nonce: number;
  hashHex: string;
  expiresAtUnix: number;
  difficultyBits: number;
  sig: string;
}

export interface PowSolution {
  nonce: number;
  hashHex: string;
}

export interface PowVerifyResponse {
  verified: boolean;
  token?: string;
  fallback?: 'lightning';
}

/* ------------------------------------------------------------------ */
/* LIGHTNING FALLBACK TYPES                                           */
/* ------------------------------------------------------------------ */

export interface AuthStartResponse {
  sessionId: string;
  invoice?: string;
  amountSats: number;
  expiresAtUnix: number;
}

export interface AuthConfirmResponse {
  verified: boolean;
  token?: string;
}

export interface LiveAuthResult {
  token: string;
  method: 'pow' | 'lightning';
  solveMs?: number;
  difficultyBits?: number;
}

export interface LightningStartResult {
  sessionId: string;
  invoice: string;
  amountSats: number;
}

/* ------------------------------------------------------------------ */
/* SERVICE                                                            */
/* ------------------------------------------------------------------ */

@Injectable({ providedIn: 'root' })
export class LiveAuthClientService {
  private readonly baseUrl = BASE_API_URL;

  private readonly headers = new HttpHeaders({
    'X-LiveAuth-PublicKey': 'la_pk_demo_public_2026',
    'Content-Type': 'application/json'
  });

  constructor(private http: HttpClient) {}

  /* =====================================================
   * MAIN ENTRY
   * ===================================================== */

  verifyHuman(): Observable<LiveAuthResult> {
    const startedAt = performance.now();

    return this.getPowChallenge().pipe(
      switchMap(challenge =>
        from(this.solvePow(challenge)).pipe(
          switchMap(solution =>
            this.verifyPow({
              challengeHex: challenge.challengeHex,
              nonce: solution.nonce,
              hashHex: solution.hashHex,
              expiresAtUnix: challenge.expiresAtUnix,
              difficultyBits: challenge.difficultyBits,
              sig: challenge.sig
            }).pipe(
              map(result => ({
                result,
                solveMs: Math.round(performance.now() - startedAt),
                difficultyBits: challenge.difficultyBits
              }))
            )
          )
        )
      ),

      switchMap(({ result, solveMs, difficultyBits }) => {
        if (result.verified && result.token) {
          return of<LiveAuthResult>({
            token: result.token,
            method: 'pow' as const,
            solveMs,
            difficultyBits
          });
        }

        if (result.fallback === 'lightning') {
          return this.startLightning().pipe(
            map(token => ({
              token,
              method: 'lightning' as const
            }) satisfies LiveAuthResult)
          );
        }

        return throwError(() => new Error('Verification failed'));
      }),

      timeout(20_000)
    );
  }

  /* =====================================================
   * POW
   * ===================================================== */

  private getPowChallenge(): Observable<PowChallengeResponse> {
    return this.http.get<PowChallengeResponse>(
      `${this.baseUrl}/api/public/pow/challenge`,
      { headers: this.headers }
    );
  }

  private verifyPow(req: PowVerifyRequest): Observable<PowVerifyResponse> {
    return this.http.post<PowVerifyResponse>(
      `${this.baseUrl}/api/public/pow/verify`,
      req,
      { headers: this.headers }
    );
  }

  private solvePow(challenge: PowChallengeResponse): Promise<PowSolution> {
    return new Promise((resolve, reject) => {
      const worker = new Worker(
        new URL('../workers/pow.worker', import.meta.url),
        { type: 'module' }
      );

      const cleanup = () => worker.terminate();

      worker.onmessage = ({ data }) => {
        if (data?.nonce !== undefined && data?.hashHex) {
          cleanup();
          resolve({ nonce: Number(data.nonce), hashHex: String(data.hashHex) });
        }
      };

      worker.onerror = err => {
        cleanup();
        reject(err);
      };

      worker.postMessage({
        projectPublicKey: challenge.projectPublicKey,
        challengeHex: challenge.challengeHex,
        targetHex: challenge.targetHex
      });
    });
  }

  /* =====================================================
   * LIGHTNING
   * ===================================================== */

  private startLightning(): Observable<string> {
    return this.http.post<AuthStartResponse>(
      `${this.baseUrl}/api/public/auth/start`,
      { userHint: 'demo-user' },
      { headers: this.headers }
    ).pipe(
      switchMap(res => this.pollLightning(res.sessionId))
    );
  }

  pollLightning(sessionId: string): Observable<string> {
    return new Observable(observer => {
      const id = setInterval(() => {
        this.http.post<AuthConfirmResponse>(
          `${this.baseUrl}/api/public/auth/confirm`,
          { sessionId },
          { headers: this.headers }
        ).subscribe(res => {
          if (res.verified && res.token) {
            clearInterval(id);
            observer.next(res.token);
            observer.complete();
          }
        });
      }, 2000);

      return () => clearInterval(id);
    });
  }

  startLightningDemo(): Observable<{
    sessionId: string;
    invoice: string;
    amountSats: number;
  }> {
    return this.http.post<any>(
      `${this.baseUrl}/api/public/demo/start`,
      {},
      {
        headers: this.headers
      }
    );
  }


}


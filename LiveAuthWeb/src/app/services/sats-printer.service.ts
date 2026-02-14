import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BASE_API_URL } from '../config';

export interface MintRequest {
  id: string;
  status: string;
  amount: number;
  mintUrl: string;
}

export interface PrintSatsRequest {
  amount: number;
  mintUrl: string;
}

@Injectable({
  providedIn: 'root'
})
export class SatsPrinterService {
  private readonly apiUrl = `${BASE_API_URL}/sats`;

  constructor(private http: HttpClient) {}

  printSats(request: PrintSatsRequest): Observable<MintRequest> {
    return this.http.post<MintRequest>(this.apiUrl, request);
  }

  getBalance(mintUrl?: string): Observable<{ balance: number; mintUrl: string }> {
    let params = new HttpParams();
    if (mintUrl) {
      params = params.set('mintUrl', mintUrl);
    }
    return this.http.get<{ balance: number; mintUrl: string }>(`${this.apiUrl}/balance`, { params });
  }
}

import { Component, OnInit } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import {DatePipe} from '@angular/common';
import { BASE_API_URL } from '../../../config';

@Component({
  selector: 'app-admin',
  templateUrl: './admin-component.html',
  imports: [
    DatePipe
  ],
  styleUrls: ['./admin-component.css']
})
export class AdminComponent implements OnInit {
  loginAttempts: any[] = [];

  constructor(private http: HttpClient) {}

  private getAuthHeaders(): { headers: HttpHeaders } {
    const token = localStorage.getItem('admin_token') || localStorage.getItem('token');
    return {
      headers: new HttpHeaders({
        Authorization: token ? `Bearer ${token}` : ''
      })
    };
  }

  ngOnInit() {
    this.http.get(`${BASE_API_URL}/api/admin/login-attempts`, this.getAuthHeaders()).subscribe({
      next: (attempts: any) => this.loginAttempts = attempts,
      error: (err) => console.error('Failed to load attempts', err)
    });
  }
}

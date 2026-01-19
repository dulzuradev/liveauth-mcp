import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import {DatePipe} from '@angular/common';

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

  ngOnInit() {
    this.http.get('http://localhost:5000/api/admin/login-attempts').subscribe({
      next: (attempts: any) => this.loginAttempts = attempts,
      error: (err) => console.error('Failed to load attempts', err)
    });
  }
}

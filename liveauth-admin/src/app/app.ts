import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import {AdminLoginComponent} from './components/admin-login-component/admin-login-component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('liveauth-admin');
}

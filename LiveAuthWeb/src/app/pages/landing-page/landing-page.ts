import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';

import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-landing-page',
  standalone: true,
  imports: [
    RouterModule,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule
  ],
  templateUrl: './landing-page.html',
  styleUrls: ['./landing-page.css']
})
export class LandingPageComponent {
  heroImageUrl = 'assets/images/liveauth_logo_4.png';

  apiSnippet = `app.post('/login', async (req, res) => {
  const session = await liveauth.start({
    publicKey: process.env.LIVEAUTH_PUBLIC_KEY,
    context: 'login'
  });

  return res.status(402).json(session);
});

app.post('/api/private', verifyLiveAuthJwt, async (req, res) => {
  return res.json(await runProtectedWork(req.user));
});`;
}

import { Routes } from '@angular/router';
import { LoginComponent } from './pages/demo/login-component/login-component';
import { MockLoginComponent } from './pages/demo/mock-login/mock-login.component';
import {DeveloperProjectsComponent} from './pages/dev/developer-projects/developer-projects';
import { VerifyEmailComponent } from './pages/dev/verify-email/verify-email.component';
import { ResendVerificationComponent } from './pages/dev/resend-verification/resend-verification.component';
import {LandingPageComponent} from './pages/landing-page/landing-page';
import {McpAgentsComponent} from './pages/mcp-agents/mcp-agents';
import {AdminComponent} from './pages/dev/admin-component/admin-component';
import {KanbanBoardComponent} from './pages/mission/kanban-board.component';
import {LegalComponent} from './pages/legal/legal';
import {BlogComponent} from './pages/blog/blog';

export const routes: Routes = [
  // Landing Page (new)
  { path: '', component: LandingPageComponent },

  // MCP Agents landing page
  { path: 'mcp-agents', component: McpAgentsComponent },

  // Blog
  { path: 'blog', component: BlogComponent },

  // Lightning Wall demo
  { path: 'demo', component: LoginComponent },

  // Mission Control (Kanban)
  { path: 'mission', component: KanbanBoardComponent },

  // Developer console
  { path: 'dev/projects', component: DeveloperProjectsComponent },

  // Email verification
  { path: 'dev/verify-email', component: VerifyEmailComponent },
  { path: 'dev/resend-verification', component: ResendVerificationComponent },

  // Admin (optional internal)
  { path: 'admin', component: AdminComponent },

  // Mock login (used after LN payment)
  { path: 'mock-login', component: MockLoginComponent },

  // Legal
  { path: 'legal', component: LegalComponent },

  // Wildcard fallback
  { path: '**', redirectTo: '' }
];

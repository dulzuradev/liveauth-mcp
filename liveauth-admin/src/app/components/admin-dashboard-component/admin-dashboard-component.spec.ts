import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { AdminDashboardComponent } from './admin-dashboard-component';
import { AdminAnalyticsService } from '../../services/admin-analytics';
import { AdminAuthService } from '../../services/admin-auth';

describe('AdminDashboardComponent', () => {
  let component: AdminDashboardComponent;
  let fixture: ComponentFixture<AdminDashboardComponent>;
  const analyticsMock = {
    getOverview: () => of({
      windowHours: 24,
      totalProjects: 0,
      activeProjects: 0,
      proProjects: 0,
      proExpired: 0,
      freeProjects: 0,
      projectsInGracePeriod: 0,
      activeAuthSessions: 0,
      pendingInvoices: 0,
      totalAuths: 0,
      successfulAuths: 0,
      failedAuths: 0,
      rateLimitHits: 0,
      totalSatsPaid: 0,
      paidAuths: 0,
      mcpSessionsTotal: 0,
      mcpSessionsActive: 0,
      mcpTokensIssued: 0,
      mcpSatsEarned: 0,
      mcpSatsEarnedUsd: null,
      mcpPaidToolCalls: 0,
      mcpPaidToolSatsCharged: 0,
      mcpDeniedToolCharges: 0,
      l402InvoicesCreated: 0,
      l402PaymentsReceived: 0,
      l402SatsEarned: 0,
      l402SatsEarnedUsd: null,
      btcUsdRate: null,
      totalSatsEarnedUsd: null,
      funnel: {
        challengesIssued: 0,
        authsStarted: 0,
        authsPaid: 0,
        authsVerified: 0,
        tokensUsed: 0,
        startToPaidRate: 0,
        paidToVerifiedRate: 0,
        verifiedToUsedRate: 0
      },
      generatedAtUtc: new Date().toISOString(),
      authsOverTime: [],
      recentEvents: []
    }),
    getProjects: () => of([]),
    getSubscriptions: () => of([])
  };
  const authMock = {
    checkStatus: () => of({ isAuthenticated: true })
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminDashboardComponent],
      providers: [
        provideRouter([]),
        { provide: AdminAnalyticsService, useValue: analyticsMock },
        { provide: AdminAuthService, useValue: authMock }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(AdminDashboardComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

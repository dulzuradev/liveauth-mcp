import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import {
  CostShieldOverviewResponse,
  DeveloperProjectsService,
  ProjectDto,
  ProtectedActionDto
} from '../../../services/developer-projects.service';
import { CostShieldProjectComponent } from './cost-shield-project';

describe('CostShieldProjectComponent', () => {
  let component: CostShieldProjectComponent;
  let fixture: ComponentFixture<CostShieldProjectComponent>;
  let service: jasmine.SpyObj<DeveloperProjectsService>;

  const project: ProjectDto = {
    projectId: '00000000-0000-0000-0000-000000000100',
    name: 'Image API',
    publicKey: 'la_pk_test',
    plan: 'free',
    monthlyQuota: 10000,
    monthlyUsed: 0,
    createdAt: new Date().toISOString(),
    environment: 'TEST',
    active: true,
    satsPerLogin: 21,
    monthlyAuthCount: 0,
    monthlyAuthPeriodStart: new Date(),
    proPaidUntil: new Date()
  };

  const action: ProtectedActionDto = {
    id: '00000000-0000-0000-0000-000000000200',
    projectId: project.projectId,
    environment: 'TEST',
    name: 'ai.generate_image',
    displayName: 'Generate Image',
    description: 'Protect image generation',
    isEnabled: true,
    baseDifficulty: 17,
    suspiciousDifficulty: 20,
    maximumDifficulty: 24,
    anonymousRequestLimit: 5,
    anonymousLimitWindowSeconds: 3600,
    authenticatedRequestLimit: null,
    authenticatedLimitWindowSeconds: null,
    requireSingleUseToken: true,
    tokenLifetimeSeconds: 120,
    allowedOrigins: ['https://app.example.com'],
    failureBehavior: 'Deny',
    allowLightningFallback: false,
    lightningPriceSats: 25,
    lightningFallbackMode: 'RateLimitOnly',
    lightningBypassesProofOfWork: true,
    estimatedCostPerExecution: 0.02,
    configurationVersion: 1,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString()
  };

  const overview: CostShieldOverviewResponse = {
    windowHours: 24,
    windowStart: new Date().toISOString(),
    windowEnd: new Date().toISOString(),
    protectedActionCount: 1,
    enabledActionCount: 1,
    challengesIssued: 10,
    challengesCompleted: 7,
    authorizationsIssued: 7,
    protectedRequests: 6,
    requestsDenied: 3,
    rateLimitedRequests: 2,
    invalidAttempts: 1,
    replayAttemptsBlocked: 1,
    estimatedProviderCostAuthorized: 0.12,
    estimatedCostAvoided: 0.06,
    challengeSuccessRate: 70,
    averageChallengeTimeMilliseconds: null,
    estimatedValues: true,
    topActions: []
  };

  beforeEach(async () => {
    service = jasmine.createSpyObj<DeveloperProjectsService>(
      'DeveloperProjectsService',
      [
        'getCostShieldOverview',
        'listProtectedActions',
        'getCostShieldEvents',
        'createProtectedAction',
        'updateProtectedAction',
        'deleteProtectedAction'
      ]);
    service.getCostShieldOverview.and.returnValue(of(overview));
    service.listProtectedActions.and.returnValue(of({ actions: [action] }));
    service.getCostShieldEvents.and.returnValue(of({
      total: 0,
      limit: 50,
      offset: 0,
      events: []
    }));

    await TestBed.configureTestingModule({
      imports: [CostShieldProjectComponent],
      providers: [
        provideNoopAnimations(),
        { provide: DeveloperProjectsService, useValue: service }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(CostShieldProjectComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('project', project);
    fixture.detectChanges();
  });

  it('loads overview, actions, and events for the selected project', () => {
    expect(service.getCostShieldOverview)
      .toHaveBeenCalledWith(project.projectId, 24);
    expect(service.listProtectedActions)
      .toHaveBeenCalledWith(project.projectId);
    expect(component.overview?.protectedRequests).toBe(6);
    expect(component.actions).toEqual([action]);
  });

  it('generates integration snippets from the selected action', () => {
    component.setSection('integration');

    expect(component.browserSnippet).toContain(project.publicKey);
    expect(component.browserSnippet).toContain(action.name);
    expect(component.serverSnippet).toContain('CostShieldVerifier');
    expect(component.serverSnippet).toContain(project.projectId);
  });

  it('opens a new action form using the project environment', () => {
    component.openCreateAction();

    expect(component.showActionDialog).toBeTrue();
    expect(component.actionForm.environment).toBe('TEST');
    expect(component.actionForm.requireSingleUseToken).toBeTrue();
  });
});

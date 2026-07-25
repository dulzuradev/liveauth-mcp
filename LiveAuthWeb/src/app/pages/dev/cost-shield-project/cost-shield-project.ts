import { CommonModule } from '@angular/common';
import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { forkJoin } from 'rxjs';

import { LocalTimePipe } from '../../../directives/local-time.pipe';
import {
  CostShieldEventDto,
  CostShieldOverviewResponse,
  DeveloperProjectsService,
  ProjectDto,
  ProtectedActionDto,
  UpsertProtectedActionRequest
} from '../../../services/developer-projects.service';

type CostShieldSection = 'overview' | 'actions' | 'events' | 'integration';

interface ProtectedActionForm extends UpsertProtectedActionRequest {
  allowedOriginsText: string;
}

@Component({
  selector: 'app-cost-shield-project',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    MessageModule,
    TableModule,
    Tag,
    ToggleSwitchModule,
    LocalTimePipe
  ],
  templateUrl: './cost-shield-project.html',
  styleUrls: ['./cost-shield-project.css']
})
export class CostShieldProjectComponent implements OnChanges {
  @Input({ required: true }) project!: ProjectDto;

  section: CostShieldSection = 'overview';
  overview: CostShieldOverviewResponse | null = null;
  actions: ProtectedActionDto[] = [];
  events: CostShieldEventDto[] = [];
  selectedIntegrationActionId = '';
  windowHours = 24;
  loading = false;
  saving = false;
  error = '';
  success = '';

  showActionDialog = false;
  editingAction: ProtectedActionDto | null = null;
  actionForm: ProtectedActionForm = this.emptyActionForm();

  constructor(private readonly projects: DeveloperProjectsService) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['project']?.currentValue?.projectId) {
      this.section = 'overview';
      this.overview = null;
      this.actions = [];
      this.events = [];
      this.selectedIntegrationActionId = '';
      this.loadAll();
    }
  }

  setSection(section: CostShieldSection): void {
    this.section = section;
    this.error = '';

    if (section === 'events' && this.events.length === 0)
      this.loadEvents();
  }

  loadAll(): void {
    if (!this.project?.projectId)
      return;

    this.loading = true;
    this.error = '';
    forkJoin({
      overview: this.projects.getCostShieldOverview(
        this.project.projectId,
        this.windowHours),
      actions: this.projects.listProtectedActions(this.project.projectId),
      events: this.projects.getCostShieldEvents(this.project.projectId, 50, 0)
    }).subscribe({
      next: ({ overview, actions, events }) => {
        this.overview = overview;
        this.actions = actions.actions;
        this.events = events.events;
        this.ensureIntegrationAction();
        this.loading = false;
      },
      error: (err) => {
        this.error = this.extractError(err, 'Failed to load CostShield.');
        this.loading = false;
      }
    });
  }

  loadOverview(windowHours: number = this.windowHours): void {
    this.windowHours = windowHours;
    this.loading = true;
    this.error = '';
    this.projects.getCostShieldOverview(
      this.project.projectId,
      windowHours).subscribe({
      next: (overview) => {
        this.overview = overview;
        this.loading = false;
      },
      error: (err) => {
        this.error = this.extractError(
          err,
          'Failed to load CostShield usage.');
        this.loading = false;
      }
    });
  }

  loadEvents(): void {
    this.loading = true;
    this.error = '';
    this.projects.getCostShieldEvents(
      this.project.projectId,
      50,
      0).subscribe({
      next: (response) => {
        this.events = response.events;
        this.loading = false;
      },
      error: (err) => {
        this.error = this.extractError(
          err,
          'Failed to load CostShield events.');
        this.loading = false;
      }
    });
  }

  openCreateAction(): void {
    this.editingAction = null;
    this.actionForm = this.emptyActionForm();
    this.showActionDialog = true;
    this.error = '';
  }

  openEditAction(action: ProtectedActionDto): void {
    this.editingAction = action;
    this.actionForm = {
      environment: action.environment,
      name: action.name,
      displayName: action.displayName,
      description: action.description,
      isEnabled: action.isEnabled,
      baseDifficulty: action.baseDifficulty,
      suspiciousDifficulty: action.suspiciousDifficulty,
      maximumDifficulty: action.maximumDifficulty,
      anonymousRequestLimit: action.anonymousRequestLimit,
      anonymousLimitWindowSeconds: action.anonymousLimitWindowSeconds,
      authenticatedRequestLimit: action.authenticatedRequestLimit,
      authenticatedLimitWindowSeconds:
        action.authenticatedLimitWindowSeconds,
      requireSingleUseToken: action.requireSingleUseToken,
      tokenLifetimeSeconds: action.tokenLifetimeSeconds,
      allowedOrigins: [...action.allowedOrigins],
      allowedOriginsText: action.allowedOrigins.join('\n'),
      failureBehavior: action.failureBehavior,
      allowLightningFallback: action.allowLightningFallback,
      lightningPriceSats: action.lightningPriceSats,
      lightningFallbackMode: action.lightningFallbackMode,
      lightningBypassesProofOfWork:
        action.lightningBypassesProofOfWork,
      estimatedCostPerExecution: action.estimatedCostPerExecution
    };
    this.showActionDialog = true;
    this.error = '';
  }

  saveAction(): void {
    const request = this.toRequest();
    if (!request) return;

    this.saving = true;
    this.error = '';
    const operation = this.editingAction
      ? this.projects.updateProtectedAction(
          this.project.projectId,
          this.editingAction.id,
          request)
      : this.projects.createProtectedAction(
          this.project.projectId,
          request);

    operation.subscribe({
      next: (saved) => {
        const index = this.actions.findIndex(action => action.id === saved.id);
        this.actions = index >= 0
          ? this.actions.map(action => action.id === saved.id ? saved : action)
          : [...this.actions, saved];
        this.actions.sort((left, right) =>
          left.name.localeCompare(right.name));
        this.ensureIntegrationAction();
        this.showActionDialog = false;
        this.saving = false;
        this.success = this.editingAction
          ? 'Protected action updated.'
          : 'Protected action created.';
        this.loadOverview();
      },
      error: (err) => {
        this.error = this.extractError(
          err,
          'Failed to save the protected action.');
        this.saving = false;
      }
    });
  }

  toggleAction(action: ProtectedActionDto): void {
    const request = this.actionToRequest(action);
    request.isEnabled = !action.isEnabled;
    this.projects.updateProtectedAction(
      this.project.projectId,
      action.id,
      request).subscribe({
      next: (saved) => {
        this.actions = this.actions.map(item =>
          item.id === saved.id ? saved : item);
        this.loadOverview();
      },
      error: (err) => {
        this.error = this.extractError(
          err,
          'Failed to change the protected action status.');
      }
    });
  }

  deleteAction(action: ProtectedActionDto): void {
    if (!confirm(
      `Delete ${action.displayName}? Actions with issued authorizations must be disabled instead.`)) {
      return;
    }

    this.projects.deleteProtectedAction(
      this.project.projectId,
      action.id).subscribe({
      next: () => {
        this.actions = this.actions.filter(item => item.id !== action.id);
        this.ensureIntegrationAction();
        this.loadOverview();
      },
      error: (err) => {
        this.error = this.extractError(
          err,
          'This action cannot be deleted. Disable it instead.');
      }
    });
  }

  copy(text: string): void {
    if (!text) return;
    navigator.clipboard.writeText(text);
    this.success = 'Copied to clipboard.';
    setTimeout(() => {
      if (this.success === 'Copied to clipboard.')
        this.success = '';
    }, 1500);
  }

  get selectedIntegrationAction(): ProtectedActionDto | null {
    return this.actions.find(
      action => action.id === this.selectedIntegrationActionId) ?? null;
  }

  get installSnippet(): string {
    return 'npm install @liveauth/sdk';
  }

  get browserSnippet(): string {
    const action = this.selectedIntegrationAction;
    if (!action) return '';
    return `import { LiveAuth } from '@liveauth/sdk';

const liveAuth = new LiveAuth({
  publicKey: '${this.project.publicKey}',
  environment: '${action.environment}'
});

const authorization = await liveAuth.protect({
  action: '${action.name}'
});

await fetch('/api/your-expensive-action', {
  method: 'POST',
  headers: {
    Authorization: \`Bearer \${authorization.token}\`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify(request)
});`;
  }

  get serverSnippet(): string {
    const action = this.selectedIntegrationAction;
    if (!action) return '';
    const origin = action.allowedOrigins[0] ?? 'https://your-app.example';
    return `const verification = await fetch(
  'https://api.liveauth.app/api/costshield/authorizations/consume',
  {
    method: 'POST',
    headers: {
      Authorization: \`Bearer \${process.env.LIVEAUTH_SECRET_KEY}\`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      token: costShieldToken,
      action: '${action.name}',
      environment: '${action.environment}',
      origin: '${origin}'
    })
  }
);

if (!verification.ok) {
  return res.status(403).json({ error: 'CostShield authorization required' });
}

// Perform the expensive provider call only after verification succeeds.`;
  }

  eventLabel(eventType: string): string {
    return eventType
      .replace(/^CostShield/, '')
      .replace(/([a-z])([A-Z])/g, '$1 $2');
  }

  eventSeverity(event: CostShieldEventDto): 'success' | 'warn' | 'danger' | 'info' {
    if (event.success) return 'success';
    if (event.eventType.includes('RateLimited')) return 'warn';
    return 'danger';
  }

  private ensureIntegrationAction(): void {
    if (!this.actions.some(
      action => action.id === this.selectedIntegrationActionId)) {
      this.selectedIntegrationActionId = this.actions[0]?.id ?? '';
    }
  }

  private toRequest(): UpsertProtectedActionRequest | null {
    const form = this.actionForm;
    if (!form.name.trim() || !form.displayName.trim()) {
      this.error = 'Action name and display name are required.';
      return null;
    }

    if (form.baseDifficulty > form.suspiciousDifficulty ||
        form.suspiciousDifficulty > form.maximumDifficulty) {
      this.error = 'Difficulty must satisfy base ≤ suspicious ≤ maximum.';
      return null;
    }

    if ((form.authenticatedRequestLimit == null) !==
        (form.authenticatedLimitWindowSeconds == null)) {
      this.error =
        'Authenticated limit and window must both be set or both be empty.';
      return null;
    }

    return {
      environment: form.environment,
      name: form.name.trim().toLowerCase(),
      displayName: form.displayName.trim(),
      description: form.description.trim(),
      isEnabled: form.isEnabled,
      baseDifficulty: Number(form.baseDifficulty),
      suspiciousDifficulty: Number(form.suspiciousDifficulty),
      maximumDifficulty: Number(form.maximumDifficulty),
      anonymousRequestLimit: Number(form.anonymousRequestLimit),
      anonymousLimitWindowSeconds:
        Number(form.anonymousLimitWindowSeconds),
      authenticatedRequestLimit:
        this.optionalNumber(form.authenticatedRequestLimit),
      authenticatedLimitWindowSeconds:
        this.optionalNumber(form.authenticatedLimitWindowSeconds),
      requireSingleUseToken: form.requireSingleUseToken,
      tokenLifetimeSeconds: Number(form.tokenLifetimeSeconds),
      allowedOrigins: form.allowedOriginsText
        .split('\n')
        .map(origin => origin.trim())
        .filter(Boolean),
      failureBehavior: form.allowLightningFallback
        ? form.failureBehavior
        : 'Deny',
      allowLightningFallback: form.allowLightningFallback,
      lightningPriceSats: Number(form.lightningPriceSats),
      lightningFallbackMode: form.lightningFallbackMode,
      lightningBypassesProofOfWork:
        form.lightningBypassesProofOfWork,
      estimatedCostPerExecution:
        Number(form.estimatedCostPerExecution)
    };
  }

  private actionToRequest(
    action: ProtectedActionDto): UpsertProtectedActionRequest {
    return {
      environment: action.environment,
      name: action.name,
      displayName: action.displayName,
      description: action.description,
      isEnabled: action.isEnabled,
      baseDifficulty: action.baseDifficulty,
      suspiciousDifficulty: action.suspiciousDifficulty,
      maximumDifficulty: action.maximumDifficulty,
      anonymousRequestLimit: action.anonymousRequestLimit,
      anonymousLimitWindowSeconds: action.anonymousLimitWindowSeconds,
      authenticatedRequestLimit: action.authenticatedRequestLimit,
      authenticatedLimitWindowSeconds:
        action.authenticatedLimitWindowSeconds,
      requireSingleUseToken: action.requireSingleUseToken,
      tokenLifetimeSeconds: action.tokenLifetimeSeconds,
      allowedOrigins: [...action.allowedOrigins],
      failureBehavior: action.failureBehavior,
      allowLightningFallback: action.allowLightningFallback,
      lightningPriceSats: action.lightningPriceSats,
      lightningFallbackMode: action.lightningFallbackMode,
      lightningBypassesProofOfWork:
        action.lightningBypassesProofOfWork,
      estimatedCostPerExecution: action.estimatedCostPerExecution
    };
  }

  private emptyActionForm(): ProtectedActionForm {
    return {
      environment: this.project?.environment ?? 'TEST',
      name: '',
      displayName: '',
      description: '',
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
      allowedOrigins: [],
      allowedOriginsText: '',
      failureBehavior: 'Deny',
      allowLightningFallback: false,
      lightningPriceSats: 25,
      lightningFallbackMode: 'RateLimitOnly',
      lightningBypassesProofOfWork: true,
      estimatedCostPerExecution: 0
    };
  }

  private optionalNumber(value: number | null): number | null {
    return value == null ? null : Number(value);
  }

  private extractError(err: any, fallback: string): string {
    const validationErrors = err?.error?.errors;
    if (validationErrors && typeof validationErrors === 'object') {
      const first = Object.values(validationErrors)
        .flat()
        .find(value => typeof value === 'string');
      if (typeof first === 'string')
        return first;
    }

    return err?.error?.message ??
      err?.error?.error_description ??
      (typeof err?.error === 'string' ? err.error : fallback);
  }
}

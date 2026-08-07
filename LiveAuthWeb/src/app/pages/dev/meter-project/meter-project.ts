import { CommonModule } from '@angular/common';
import { Component, Input, OnChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  DeveloperProjectsService, MeterAnalytics, MeterReceipt, MeterRouteRule,
  MeterSettings, UpsertMeterRouteRule
} from '../../../services/developer-projects.service';

type MeterPage = 'overview' | 'routes' | 'origin' | 'lightning' | 'receipts' | 'webhooks' | 'settings';

@Component({
  selector: 'app-meter-project', standalone: true, imports: [CommonModule, FormsModule],
  templateUrl: './meter-project.html', styleUrls: ['./meter-project.css']
})
export class MeterProjectComponent implements OnChanges {
  @Input({ required: true }) projectId = '';
  @Input() publicKey = '';
  page: MeterPage = 'overview';
  settings: MeterSettings | null = null;
  routes: MeterRouteRule[] = [];
  receipts: MeterReceipt[] = [];
  analytics: MeterAnalytics | null = null;
  loading = false; saving = false; error = ''; notice = '';
  routeForm: UpsertMeterRouteRule = this.emptyRoute();
  editingRouteId = '';
  lightning = { providerType: 'LND_REST', displayName: 'Merchant LND', restUrl: '', tlsCertificate: '', macaroon: '', supportsPaymentLookup: true };

  constructor(private readonly api: DeveloperProjectsService) {}
  ngOnChanges() { if (this.projectId) this.load(); }

  load() {
    this.loading = true; this.error = '';
    this.api.getMeterSettings(this.projectId).subscribe({
      next: settings => {
        this.settings = settings;
        if (settings.lightningConnection) {
          this.lightning.displayName = settings.lightningConnection.displayName;
          this.lightning.restUrl = settings.lightningConnection.restUrl;
        }
        this.loading = false; this.loadRoutes(); this.loadOverview();
      }, error: err => { this.error = err?.error?.message ?? 'Unable to load Meter.'; this.loading = false; }
    });
  }
  select(page: MeterPage) {
    this.page = page; this.notice = ''; this.error = '';
    if (page === 'overview') this.loadOverview();
    if (page === 'routes') this.loadRoutes();
    if (page === 'receipts') this.loadReceipts();
  }
  saveSettings() {
    if (!this.settings) return; this.saving = true; this.error = '';
    this.api.updateMeterSettings(this.projectId, this.settings).subscribe({
      next: value => { this.settings = value; this.notice = 'Meter settings saved.'; this.saving = false; },
      error: err => { this.error = this.message(err); this.saving = false; }
    });
  }
  saveRoute() {
    const request = this.editingRouteId
      ? this.api.updateMeterRoute(this.projectId, this.editingRouteId, this.routeForm)
      : this.api.createMeterRoute(this.projectId, this.routeForm);
    request.subscribe({ next: () => { this.routeForm = this.emptyRoute(); this.editingRouteId = ''; this.notice = 'Route saved.'; this.loadRoutes(); },
      error: err => this.error = this.message(err) });
  }
  editRoute(route: MeterRouteRule) {
    const { id, createdAt, updatedAt, ...form } = route; this.routeForm = { ...form }; this.editingRouteId = id;
  }
  deleteRoute(route: MeterRouteRule) {
    if (!confirm(`Delete ${route.httpMethod} ${route.pathPattern}?`)) return;
    this.api.deleteMeterRoute(this.projectId, route.id).subscribe({ next: () => this.loadRoutes(), error: err => this.error = this.message(err) });
  }
  saveLightning() {
    this.saving = true; this.api.updateMeterLightning(this.projectId, this.lightning).subscribe({
      next: connection => {
        if (this.settings) this.settings.lightningConnection = connection;
        this.lightning.macaroon = ''; this.lightning.tlsCertificate = '';
        this.notice = 'Merchant Lightning connection saved. Secret contents are no longer available.'; this.saving = false;
      }, error: err => { this.error = this.message(err); this.saving = false; }
    });
  }
  testLightning() {
    this.api.testMeterLightning(this.projectId).subscribe({
      next: result => this.notice = result.success ? `Connected${result.alias ? ` to ${result.alias}` : ''}.` : (result.error ?? 'Connection failed.'),
      error: err => this.error = this.message(err)
    });
  }
  testWebhook() {
    this.api.testMeterWebhook(this.projectId).subscribe({ next: () => this.notice = 'Test webhook queued.', error: err => this.error = this.message(err) });
  }
  get localGateway() { return `/gateway/${this.publicKey || this.projectId}`; }
  private loadRoutes() { this.api.getMeterRoutes(this.projectId).subscribe({ next: x => this.routes = x, error: err => this.error = this.message(err) }); }
  private loadReceipts() { this.api.getMeterReceipts(this.projectId).subscribe({ next: x => this.receipts = x, error: err => this.error = this.message(err) }); }
  private loadOverview() { this.api.getMeterAnalytics(this.projectId).subscribe({ next: x => this.analytics = x, error: err => this.error = this.message(err) }); }
  private emptyRoute(): UpsertMeterRouteRule { return { httpMethod: 'GET', pathPattern: '/health', priceSats: 0, freeRequestAllowance: 0, enabled: true, priority: 0, credentialLifetimeSeconds: 3600, maximumCredentialUses: 1, bindRequestBody: false }; }
  private message(error: any) { return error?.error?.detail ?? error?.error?.message ?? 'Meter request failed.'; }
}

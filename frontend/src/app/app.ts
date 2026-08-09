import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Account, Integration, Invoice, Meter, Overview } from './models';

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private hub?: HubConnection;

  readonly overview = signal<Overview | null>(null);
  readonly accounts = signal<Account[]>([]);
  readonly meters = signal<Meter[]>([]);
  readonly integrations = signal<Integration[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly live = signal(false);
  readonly notice = signal<{ kind: 'ok' | 'error'; text: string } | null>(null);
  readonly navOpen = signal(false);
  readonly maxRevenue = computed(() => Math.max(1, ...(this.overview()?.revenue.map(x => Math.max(x.actual, x.forecast)) ?? [1])));

  usageForm = {
    eventId: this.nextEventId(),
    accountId: 'acme-labs',
    meter: 'api_calls',
    quantity: 15000
  };
  billingAccount = 'acme-labs';

  ngOnInit(): void {
    this.reload();
    this.connectLiveUpdates();
  }

  ngOnDestroy(): void {
    this.hub?.stop();
  }

  reload(): void {
    this.loading.set(true);
    const requests = [
      this.http.get<Overview>('/api/overview').subscribe({ next: value => { this.overview.set(value); this.loading.set(false); }, error: () => { this.loading.set(false); this.showError('API is not available.'); } }),
      this.http.get<Account[]>('/api/accounts').subscribe(value => this.accounts.set(value)),
      this.http.get<Meter[]>('/api/meters').subscribe(value => this.meters.set(value)),
      this.http.get<Integration[]>('/api/integrations').subscribe(value => this.integrations.set(value))
    ];
    void requests;
  }

  async connectLiveUpdates(): Promise<void> {
    this.hub = new HubConnectionBuilder().withUrl('/hubs/billing').withAutomaticReconnect().build();
    this.hub.on('usageRecorded', () => this.reload());
    this.hub.on('invoiceChanged', () => this.reload());
    this.hub.onreconnecting(() => this.live.set(false));
    this.hub.onreconnected(() => this.live.set(true));
    this.hub.onclose(() => this.live.set(false));
    try { await this.hub.start(); this.live.set(this.hub.state === HubConnectionState.Connected); }
    catch { this.live.set(false); }
  }

  recordUsage(): void {
    if (!this.usageForm.eventId.trim() || this.usageForm.quantity <= 0) return;
    this.busy.set(true);
    this.http.post<{ duplicate: boolean }>('/api/usage-events', {
      ...this.usageForm,
      occurredAt: new Date().toISOString(),
      dimensions: { source: 'dashboard' }
    }).subscribe({
      next: result => {
        this.busy.set(false);
        this.notice.set({ kind: 'ok', text: result.duplicate ? 'This event was already recorded.' : 'Usage event recorded.' });
        this.usageForm.eventId = this.nextEventId();
        this.reload();
      },
      error: response => { this.busy.set(false); this.showError(response.error?.message ?? 'The event could not be recorded.'); }
    });
  }

  runBilling(): void {
    const now = new Date();
    const start = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`;
    const endDate = new Date(now.getFullYear(), now.getMonth() + 1, 0);
    const end = `${endDate.getFullYear()}-${String(endDate.getMonth() + 1).padStart(2, '0')}-${String(endDate.getDate()).padStart(2, '0')}`;
    this.busy.set(true);
    this.http.post<Invoice>('/api/billing-runs', { accountId: this.billingAccount, periodStart: start, periodEnd: end, issueImmediately: false }).subscribe({
      next: invoice => { this.busy.set(false); this.notice.set({ kind: 'ok', text: `${invoice.number} is ready.` }); this.reload(); },
      error: response => { this.busy.set(false); this.showError(response.error?.message ?? 'Billing run failed.'); }
    });
  }

  issueInvoice(invoice: Invoice): void {
    this.changeInvoice(invoice, 'issue');
  }

  markPaid(invoice: Invoice): void {
    this.changeInvoice(invoice, 'paid');
  }

  private changeInvoice(invoice: Invoice, action: 'issue' | 'paid'): void {
    this.busy.set(true);
    this.http.post<Invoice>(`/api/invoices/${invoice.id}/${action}`, {}).subscribe({
      next: updated => { this.busy.set(false); this.notice.set({ kind: 'ok', text: `${updated.number}: ${updated.status}.` }); this.reload(); },
      error: response => { this.busy.set(false); this.showError(response.error?.message ?? 'Invoice update failed.'); }
    });
  }

  barHeight(value: number): number {
    return Math.max(4, Math.round(value / this.maxRevenue() * 142));
  }

  meterName(key: string): string {
    return this.meters().find(x => x.key === key)?.name ?? key.replaceAll('_', ' ');
  }

  shortNumber(value: number): string {
    return Intl.NumberFormat('en', { notation: 'compact', maximumFractionDigits: 1 }).format(value);
  }

  trackById(_: number, item: { id?: string; eventId?: string; key?: string }): string {
    return item.id ?? item.eventId ?? item.key ?? '';
  }

  closeNotice(): void { this.notice.set(null); }
  toggleNav(): void { this.navOpen.update(value => !value); }

  private showError(text: string): void { this.notice.set({ kind: 'error', text }); }
  private nextEventId(): string { return `evt_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 7)}`; }
}
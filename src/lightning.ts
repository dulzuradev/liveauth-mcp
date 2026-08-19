import QRCode from 'qrcode';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

export type LightningPaymentStatus = 'pending' | 'paid' | 'expired';

export interface LightningPaymentDetails {
  invoice: string;
  lightningUri: string;
  amountSats?: number;
  expiresAt?: string;
  expiresAtUnix?: number;
  status: LightningPaymentStatus;
}

export interface LightningAwareResult extends Record<string, unknown> {
  lightning?: LightningPaymentDetails;
}

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined;
}

function asFiniteNumber(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

export function toLightningUri(invoice: string): string {
  const normalized = invoice.trim();
  if (!/^[A-Za-z0-9]+$/.test(normalized) || !normalized.toLowerCase().startsWith('ln')) {
    throw new Error('Invalid BOLT11 Lightning invoice');
  }
  return `lightning:${normalized}`;
}

export function mapLightningStatus(
  status: unknown,
  expiresAtUnix?: number,
  nowMs = Date.now()
): LightningPaymentStatus {
  const normalized = typeof status === 'string' ? status.toLowerCase() : '';
  if (normalized === 'paid' || normalized === 'confirmed' || normalized === 'settled' || normalized === 'l402_paid') {
    return 'paid';
  }
  if (normalized === 'expired' || (expiresAtUnix !== undefined && expiresAtUnix * 1000 <= nowMs)) {
    return 'expired';
  }
  return 'pending';
}

export function extractLightningDetails(
  value: Record<string, unknown>,
  cached?: LightningPaymentDetails,
  nowMs = Date.now()
): LightningPaymentDetails | undefined {
  const invoiceObject = asRecord(value.invoice);
  const invoice =
    (typeof invoiceObject?.bolt11 === 'string' ? invoiceObject.bolt11 : undefined) ??
    (typeof value.pr === 'string' ? value.pr : undefined) ??
    cached?.invoice;

  if (!invoice) return undefined;

  const expiresAtUnix =
    asFiniteNumber(invoiceObject?.expiresAtUnix) ??
    asFiniteNumber(value.expiresAtUnix) ??
    cached?.expiresAtUnix;
  const amountSats =
    asFiniteNumber(invoiceObject?.amountSats) ??
    asFiniteNumber(value.amountSats) ??
    cached?.amountSats;
  const status = mapLightningStatus(value.paymentStatus ?? value.status, expiresAtUnix, nowMs);

  return {
    invoice,
    lightningUri: toLightningUri(invoice),
    ...(amountSats === undefined ? {} : { amountSats }),
    ...(expiresAtUnix === undefined
      ? (cached?.expiresAt ? { expiresAt: cached.expiresAt } : {})
      : { expiresAt: new Date(expiresAtUnix * 1000).toISOString(), expiresAtUnix }),
    status,
  };
}

export function withLightningDetails(
  value: Record<string, unknown>,
  cached?: LightningPaymentDetails,
  nowMs = Date.now()
): LightningAwareResult {
  const lightning = extractLightningDetails(value, cached, nowMs);
  return lightning ? { ...value, lightning } : { ...value };
}

export async function toToolResult(value: LightningAwareResult): Promise<CallToolResult> {
  const content: CallToolResult['content'] = [
    { type: 'text', text: JSON.stringify(value, null, 2) },
  ];

  if (value.lightning?.status === 'pending') {
    const png = await QRCode.toBuffer(value.lightning.lightningUri, {
      errorCorrectionLevel: 'M',
      margin: 1,
      type: 'png',
      width: 280,
    });
    content.push({ type: 'image', data: png.toString('base64'), mimeType: 'image/png' });
  }

  return { content, structuredContent: value };
}

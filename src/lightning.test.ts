import { describe, expect, it } from 'vitest';
import {
  mapLightningStatus,
  toLightningUri,
  toToolResult,
  withLightningDetails,
} from './lightning.js';

describe('portable Lightning responses', () => {
  it('formats and validates lightning: wallet URIs', () => {
    expect(toLightningUri('lnbc1invoice')).toBe('lightning:lnbc1invoice');
    expect(() => toLightningUri('lnbc1invoice\nhttps://evil.example')).toThrow('Invalid BOLT11');
  });

  it('preserves invoice data while adding amount, expiration, and pending state', () => {
    const result = withLightningDetails({
      quoteId: 'quote-1',
      invoice: {
        bolt11: 'lnbc1invoice',
        amountSats: 21,
        expiresAtUnix: 1_900_000_000,
        paymentHash: 'hash',
      },
    }, undefined, 1_800_000_000_000);

    expect(result.invoice).toMatchObject({ bolt11: 'lnbc1invoice', paymentHash: 'hash' });
    expect(result.lightning).toEqual({
      invoice: 'lnbc1invoice',
      lightningUri: 'lightning:lnbc1invoice',
      amountSats: 21,
      expiresAt: '2030-03-17T17:46:40.000Z',
      expiresAtUnix: 1_900_000_000,
      status: 'pending',
    });
  });

  it('maps paid, pending, and expired states without losing cached invoice context', () => {
    expect(mapLightningStatus('paid', 1_700_000_000, 1_800_000_000_000)).toBe('paid');
    expect(mapLightningStatus('pending', 1_900_000_000, 1_800_000_000_000)).toBe('pending');
    expect(mapLightningStatus('pending', 1_700_000_000, 1_800_000_000_000)).toBe('expired');
  });

  it('emits a QR image only while an invoice is payable', async () => {
    const result = await toToolResult({
      quoteId: 'quote-1',
      lightning: {
        invoice: 'lnbc1invoice',
        lightningUri: 'lightning:lnbc1invoice',
        amountSats: 21,
        status: 'pending',
      },
    });
    expect(result.content).toContainEqual(expect.objectContaining({ type: 'image', mimeType: 'image/png' }));
    expect(result.structuredContent).toMatchObject({ lightning: { amountSats: 21, status: 'pending' } });
  });
});

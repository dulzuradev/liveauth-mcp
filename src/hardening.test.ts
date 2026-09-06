import { describe, it, expect, vi } from 'vitest';
import { createMcpGate, ChargeDeniedError, BudgetExceededError, ToolExecutionError } from './index.js';

const makeGate = (body: object, status = 200) => createMcpGate({
  publicKey: 'test', fetch: async () => new Response(JSON.stringify(body), { status }),
});

describe('charge contract', () => {
  it.each(['tool_inactive', 'tool_unpublished', 'tool_not_found', 'budget_exceeded', 'rate_limited', 'denied', 'future_reason'])(
    'preserves denial %s without executing', async (reason) => {
      const handler = vi.fn();
      const gate = makeGate({ status: 'deny', reason, toolName: 'dns_lookup', callsUsed: 0, satsUsed: 0 }, reason === 'tool_not_found' ? 404 : reason === 'rate_limited' ? 429 : 200);
      const error = await gate.invoke('jwt', {}, handler, {}, { validateFirst: false }).catch(e => e);
      expect(error).toBeInstanceOf(ChargeDeniedError);
      expect(error).toBeInstanceOf(BudgetExceededError);
      expect(error.reason).toBe(reason);
      expect(error.code).toBe(reason);
      expect(error.toolName).toBe('dns_lookup');
      expect(handler).not.toHaveBeenCalled();
    });
  it('preserves billing and independent receipt IDs on synchronous and asynchronous failures', async () => {
    const charge = { status: 'ok', callsUsed: 1, satsUsed: 1, grossSats: 1, revenueEventId: 'event', receipt: { body: { idempotencyKey: 'retry', requestId: 'server-id' } } };
    const gate = makeGate(charge);
    for (const handler of [() => { throw new Error('private failure'); }, async () => { throw new Error('private failure'); }]) {
      const error = await gate.invoke('jwt-secret', {}, handler, {}, { validateFirst: false, idempotencyKey: 'retry' }).catch(e => e);
      expect(error).toBeInstanceOf(ToolExecutionError);
      expect(error.charge.revenueEventId).toBe('event');
      expect(error.charge.grossSats).toBe(1);
      expect(error.charge.receipt.body.idempotencyKey).toBe('retry');
      expect(error.charge.receipt.body.requestId).toBe('server-id');
      expect(error.idempotencyKey).toBe('retry');
      expect(error.cause.message).toBe('private failure');
      expect(JSON.stringify(error)).not.toContain('private failure');
      expect(JSON.stringify(error)).not.toContain('jwt-secret');
    }
  });
  it('returns a successful handler value unchanged', async () => {
    expect(await makeGate({status: 'ok'}).invoke('jwt', {}, () => 42, {}, {validateFirst: false})).toBe(42);
  });
  it('uses a generic denial when no reason is supplied', async () => {
    const error = await makeGate({status: 'deny', callsUsed: 0, satsUsed: 0}).invoke('jwt', {}, () => 42, {}, {validateFirst: false}).catch(e => e);
    expect(error.reason).toBe('denied');
    expect(error.message).not.toContain('budget');
  });
  it('does not turn HTTP authentication failures into charge denials', async () => {
    const error = await makeGate({message: 'Unauthorized'}, 401).charge('jwt', 1).catch(e => e);
    expect(error.status).toBe(401);
    expect(error).not.toBeInstanceOf(ChargeDeniedError);
  });
  it('returns HTTP structured denials directly from charge()', async () => {
    const result = await makeGate({status: 'deny', reason: 'tool_not_found', toolId: 'missing', callsUsed: 0, satsUsed: 0}, 404).charge('jwt', 1);
    expect(result.ok).toBe(false);
    expect(result.reason).toBe('tool_not_found');
    expect(result.toolId).toBe('missing');
  });

});

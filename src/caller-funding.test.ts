import { describe, expect, it, vi } from 'vitest';
import { LiveAuthMcpServerGate, requestHash } from './server-gate.js';
import { LiveAuthMcpClient } from './client.js';
import { toMcpPaymentResult } from './payment-result.js';
import { ChargeDeniedError, PaidOperationReplayError } from './errors.js';

const paid = { status: 'ok', fundingMode: 'caller', callsUsed: 1, satsUsed: 2, grossSats: 2,
  netSats: 2, revenueEventId: 'revenue', callerBalanceSats: 0, remainingBudgetSats: 9998,
  receipt: { body: { fundingMode: 'caller', providerProjectId: 'provider', payingProjectId: null, mcpGateSessionId: 'caller' } } };
const challenge = { status: 'deny', fundingMode: 'caller', reason: 'payment_required', callsUsed: 0, satsUsed: 0,
  payment: { paymentId: 'payment', method: 'lightning', invoice: 'ln-test', amountSats: 2, creditSats: 2,
    expiresAt: new Date(Date.now()+600000).toISOString(), confirmPath: '/api/mcp/payments/payment/confirm', retryIdempotencyKey: 'operation' } };
function setup(response: object = paid, mode: 'caller' | 'provider' = 'caller') {
  const fetch = vi.fn(async (url: any) => new Response(JSON.stringify(
    String(url).endsWith('/capabilities') ? { callerFunding: true } : response), { status: String(url).endsWith('/charge') && response === challenge ? 402 : 200 }));
  const gate = new LiveAuthMcpServerGate({ publicKey: 'la_pk_provider', fundingMode: mode, toolName: 'inspect', fetch });
  const handler = vi.fn(async () => ({ answer: true }));
  const call = (jwt='caller', costSats=2) => gate.invoke(jwt, { url: 'https://example.com' }, handler, {}, { costSats, idempotencyKey: 'operation', validateFirst: false });
  return { fetch, gate, handler, call };
}
describe('reusable caller-funded gate', () => {
  it('binds funding, configured price, input and retry identity; returns caller receipt', async () => {
    const {call,fetch,handler} = setup(); await call(); expect(handler).toHaveBeenCalledOnce();
    const init = fetch.mock.calls.find(c => String(c[0]).endsWith('/charge')) as any;
    expect(JSON.parse(init[1].body)).toMatchObject({ fundingMode:'caller', callCostSats:2, idempotencyKey:'operation', requestHash:requestHash({url:'https://example.com'}) });
    expect(init[1].headers['X-LW-Secret']).toBeUndefined();
    expect(handler.mock.calls[0][1].liveAuth.charge).toMatchObject({netSats:2, revenueEventId:'revenue',receipt:{body:{payingProjectId:null}}});
  });
  it('returns an MCP-safe, model-visible invoice without executing an unfunded tool', async () => {
    const {call,handler} = setup(challenge); const error=await call().catch(e=>e);
    expect(error).toBeInstanceOf(ChargeDeniedError); expect(handler).not.toHaveBeenCalled();
    const result=toMcpPaymentResult(error)!; expect(result.isError).toBe(true);
    expect(result.structuredContent.liveauth.payment).toMatchObject({paymentId:'payment',amountSats:2});
    expect(JSON.parse(result.content[0].text).liveauth.reason).toBe('payment_required');
  });
  it('confirms payment on the same caller session, then retries the original request', async () => {
    let confirmed=false;
    const fetch=vi.fn(async (url:any,init:any) => {
      if(String(url).endsWith('/capabilities')) return Response.json({callerFunding:true});
      if(String(url).endsWith('/confirm')) { expect(init.headers.Authorization).toBe('Bearer caller-A'); confirmed=true; return Response.json({status:'paid',callerBalanceSats:2}); }
      return Response.json(confirmed?paid:challenge,{status:confirmed?200:402});
    });
    const gate=new LiveAuthMcpServerGate({publicKey:'la_pk_provider',fundingMode:'caller',toolName:'inspect',fetch});
    const handler=vi.fn(async()=>42);const call=()=>gate.invoke('caller-A',{},handler,{}, {idempotencyKey:'operation',validateFirst:false});
    await expect(call()).rejects.toBeInstanceOf(ChargeDeniedError);
    const client=new LiveAuthMcpClient({publicKey:'la_pk_provider',fetch,autoRefresh:false});client.setToken('caller-A');
    await client.confirmPayment('payment');expect(await call()).toBe(42);expect(handler).toHaveBeenCalledOnce();
    const bodies=fetch.mock.calls.filter(c=>String(c[0]).endsWith('/charge')).map(c=>JSON.parse((c[1] as any).body));expect(bodies[0]).toEqual(bodies[1]);
  });
  it('does not execute duplicate paid operations and preserves their receipt/status', async () => {
    const {call,handler}=setup({...paid,duplicate:true});const error=await call().catch(e=>e);
    expect(error).toBeInstanceOf(PaidOperationReplayError);expect(handler).not.toHaveBeenCalled();
    expect(toMcpPaymentResult(error)?._meta.liveauth).toMatchObject({billed:true,duplicate:true,revenueEventId:'revenue'});
  });
  it.each(['payment_expired','idempotency_conflict','price_mismatch','provider_mismatch','budget_exceeded'])('fails closed on %s',async(reason)=>{
    const {call,handler}=setup({...challenge,reason,payment:null});await expect(call()).rejects.toMatchObject({code:reason});expect(handler).not.toHaveBeenCalled();
  });
  it('never substitutes a project key for the individual caller bearer token',async()=>{
    const {call,fetch}=setup();await call('caller-A');await call('caller-B');
    const calls=fetch.mock.calls.filter(c=>String(c[0]).endsWith('/charge')) as any[];
    expect(calls.map(c=>c[1].headers.Authorization)).toEqual(['Bearer caller-A','Bearer caller-B']);
  });
  it('supports zero-sat tools',async()=>{const {call,handler}=setup({...paid,grossSats:0,netSats:0,satsUsed:0});await call('caller',0);expect(handler).toHaveBeenCalledOnce();});
  it('preserves explicit provider-funded integrations',async()=>{const {call,fetch,handler}=setup({...paid,fundingMode:'provider'},'provider');await call();expect(handler).toHaveBeenCalledOnce();expect(fetch.mock.calls).toHaveLength(1);});
  it('refuses an older backend before a potentially provider-funded charge',async()=>{
    const fetch=vi.fn(async()=>Response.json({}));const gate=new LiveAuthMcpServerGate({publicKey:'pk',fundingMode:'caller',fetch});
    await expect(gate.invoke('jwt',{},async()=>42,{}, {validateFirst:false,idempotencyKey:'key'})).rejects.toMatchObject({code:'caller_funding_unavailable'});
    expect(fetch).toHaveBeenCalledTimes(1);
  });
  it('canonicalizes JSON object key order but preserves distinct arguments',()=>{
    expect(requestHash({a:1,b:{x:2,y:3}})).toBe(requestHash({b:{y:3,x:2},a:1}));
    expect(requestHash({a:1})).not.toBe(requestHash({a:2}));
  });
});

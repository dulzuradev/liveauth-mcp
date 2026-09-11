import { ChargeDeniedError, PaidOperationReplayError } from './errors.js';

/** MCP-native, model-visible payment result. No JWT, secret, preimage, or cause. */
export function toMcpPaymentResult(error: unknown) {
  if (!(error instanceof ChargeDeniedError) && !(error instanceof PaidOperationReplayError)) return undefined;
  const details = error instanceof PaidOperationReplayError ? error.charge : error.details as import('./types.js').McpChargeResult;
  const liveauth = {
    status: details.status, reason: error.code, fundingMode: details.fundingMode,
    billed: details.status === 'ok', duplicate: details.duplicate ?? false,
    toolId: details.toolId, toolName: details.toolName, grossSats: details.grossSats,
    revenueEventId: details.revenueEventId, callerBalanceSats: details.callerBalanceSats,
    remainingBudgetSats: details.remainingBudgetSats,
    ...(details.payment ? { payment: details.payment } : {}),
    ...(details.receipt ? { receipt: details.receipt } : {}),
  };
  return {
    isError: true as const,
    content: [{ type: 'text' as const, text: JSON.stringify({ liveauth }) }],
    structuredContent: { liveauth },
    _meta: { liveauth },
  };
}

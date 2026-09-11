import { createMcpGate, toMcpPaymentResult, mcpIdempotencyKey } from '@liveauth-labs/mcp-server';

// Register this same provider/tool price in LiveAuth before accepting calls.
const gate = createMcpGate({
  publicKey: process.env.LIVEAUTH_PUBLIC_KEY,
  baseUrl: process.env.LIVEAUTH_API_URL,
  toolName: 'http_inspect',
  fundingMode: 'caller',
});

// Adapt your MCP framework's authenticated request context to these fields.
export function paidHandler(execute) {
  return async (validatedInput, { callerJwt, requestIdHeader, rpcId }) => {
    try {
      const output = await gate.invoke(callerJwt, validatedInput,
        async (input, ctx) => ({ output: await execute(input), charge: ctx.liveAuth.charge }),
        {}, { costSats: 2, idempotencyKey: mcpIdempotencyKey(requestIdHeader, rpcId) });
      return { content: [{ type: 'text', text: JSON.stringify(output.output) }], _meta: { liveauth: output.charge } };
    } catch (error) {
      const payment = toMcpPaymentResult(error);
      if (payment) return payment; // MCP connection/discovery remain usable.
      throw error;
    }
  };
}

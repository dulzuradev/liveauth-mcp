# First-User Validation Path: MCP Lightning Payments

Positioning wedge: **Monetize MCP tools with Lightning payments.**

Goal: get one real MCP/tool developer to try a real paid call flow, then talk to them before building more product surface.

## Current Verification

Checked production on 2026-05-28 UTC:

- `GET https://api.liveauth.app/api/health` returned healthy.
- LND is connected, `LndUseMock=false`, and the API reports one active channel and one peer.
- `POST /api/mcp/start` with `X-LW-Public: la_pk_demo` and `{ "forceLightning": true }` returned a real 10 sat BOLT11 invoice plus a `quoteId`.
- `POST /api/mcp/confirm` before payment returned `paymentStatus: "pending"` and no JWT, as expected.
- `POST /api/public/l402/invoice?amountSats=1` with `X-LW-Public: la_pk_demo` returned a real 1 sat BOLT11 invoice and payment hash.
- `POST /api/public/l402/validate` before payment returned HTTP 402, as expected.
- The older `POST /api/public/demo/start` endpoint currently returns HTTP 404 in production. Do not use it for the public demo path.

Blockers to finish the true end-to-end proof:

- No Lightning wallet CLI was available locally (`lncli`, `lightning-cli`, `lnget`, `phoenixd`, `litcli`, and `bos` were not found).
- The E2E-suite public key `la_pk_XSay0x837ww6pYb8kX7iu95t` is no longer valid in production.
- To finish verification, pay a fresh production invoice from a real wallet, then confirm that `/api/mcp/confirm` issues an MCP JWT and `/api/mcp/charge` accepts it.

## Manual Real-Payment Script

Use this for the smallest credible public demo. Replace `la_pk_demo` with a real first-user project key when one exists.

```bash
export LIVEAUTH_API_URL="https://api.liveauth.app"
export LIVEAUTH_PUBLIC_KEY="la_pk_demo"

curl -sS -X POST "$LIVEAUTH_API_URL/api/mcp/start" \
  -H "Content-Type: application/json" \
  -H "X-LW-Public: $LIVEAUTH_PUBLIC_KEY" \
  --data '{"forceLightning":true}'
```

Copy the returned `invoice.bolt11` into a real Lightning wallet and pay it before `expiresAtUnix`.

```bash
export QUOTE_ID="paste-returned-quote-id"

curl -sS -X POST "$LIVEAUTH_API_URL/api/mcp/confirm" \
  -H "Content-Type: application/json" \
  -H "X-LW-Public: $LIVEAUTH_PUBLIC_KEY" \
  --data "{\"quoteId\":\"$QUOTE_ID\"}"
```

If the payment settled, copy the returned `jwt`:

```bash
export LIVEAUTH_JWT="paste-returned-jwt"

curl -sS "$LIVEAUTH_API_URL/api/mcp/usage" \
  -H "Authorization: Bearer $LIVEAUTH_JWT" \
  -H "X-LW-Public: $LIVEAUTH_PUBLIC_KEY"

curl -sS -X POST "$LIVEAUTH_API_URL/api/mcp/charge" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $LIVEAUTH_JWT" \
  -H "X-LW-Public: $LIVEAUTH_PUBLIC_KEY" \
  --data '{"callCostSats":1}'
```

Demo story:

1. Tool developer sets a sats-per-call price.
2. Agent requests access and receives a Lightning invoice.
3. User or agent wallet pays.
4. LiveAuth issues an MCP JWT.
5. The protected MCP tool validates the JWT and charges one call.

## Launch Drafts

Hacker News:

> Show HN: LiveAuth - monetize MCP tools with Lightning payments
>
> I am building LiveAuth, a tiny payment/auth layer for MCP tools.
>
> The idea: an MCP tool can ask for a Lightning payment, LiveAuth verifies settlement, issues a short-lived MCP JWT, and the tool charges per call in sats. No account signup or card checkout required for the caller.
>
> I am looking for 3-5 MCP/tool developers who want to charge for search, scraping, browser automation, data access, or agent workflows. The current goal is not feature breadth; it is proving one real paid MCP call end to end.
>
> Demo path: invoice -> payment -> MCP JWT -> protected tool call -> per-call charge.

Twitter/X:

> Building LiveAuth: monetize MCP tools with Lightning payments.
>
> MCP tool calls should be able to cost 1-10 sats without accounts, cards, or a hosted proxy.
>
> Flow: BOLT11 invoice -> paid -> MCP JWT -> protected tool call -> per-call charge.
>
> Looking for MCP server/tool builders who want to try the first real demo.

LinkedIn:

> I am shifting LiveAuth from feature-building to first-user validation.
>
> The wedge is simple: monetize MCP tools with Lightning payments.
>
> If you build MCP servers, agent tools, search/scraping APIs, browser automation, or other pay-per-use AI infrastructure, I would like to test one real flow with you: Lightning invoice, payment confirmation, MCP JWT, protected tool call, and per-call charging.
>
> The goal is not another dashboard. It is the smallest credible path for developers to charge agents for useful work.

Direct outreach:

> Hey, I saw your MCP/tooling work on <project>. I am building LiveAuth, a small layer for charging MCP tool calls with Lightning.
>
> The demo is intentionally narrow: your tool sets a sats-per-call price, the caller pays a Lightning invoice, LiveAuth issues an MCP JWT, and your server validates/charges the call.
>
> Would you be open to trying it on one low-risk tool endpoint? I am looking for blunt first-user feedback, not a polished sales call.

## Target Developers And Projects

Prioritize people already exposing useful tools where per-call pricing makes sense.

| Target | Why it fits | Link |
| --- | --- | --- |
| Apify MCP Server | Already connects agents to paid scraping/automation actors; closest monetization analogue. | https://github.com/apify/apify-mcp-server |
| Apify MCP servers collection | Explicitly frames MCP servers as published and monetized on Apify. | https://github.com/apify/mcp-servers |
| Browserbase MCP Server | Browser sessions cost real infrastructure money; hosted MCP is a natural paid-call use case. | https://github.com/browserbase/mcp-server-browserbase |
| Firecrawl MCP Server | Search/scrape/crawl tools map cleanly to sats-per-call or sats-per-crawl. | https://github.com/firecrawl/firecrawl-mcp-server |
| Exa MCP Server | Search and research calls are API-metered and agent-native. | https://github.com/exa-labs/exa-mcp-server |
| Supabase MCP Server | Database/project operations are high-value and need tight auth boundaries. | https://github.com/supabase-community/supabase-mcp |
| Upstash Context7 | Documentation retrieval has obvious usage-based cost and strong MCP adoption. | https://github.com/upstash/context7 |
| GitHub MCP Server | High-visibility official server; good ecosystem feedback even if not first monetization user. | https://github.com/github/github-mcp-server |
| Stripe MCP / Agent Toolkit | Payment-tool builders will understand the wedge and can stress-test auth/payment semantics. | https://docs.stripe.com/mcp |
| Model Context Protocol servers | Reference/community server maintainers can point to serious early adopters. | https://github.com/modelcontextprotocol/servers |
| Smithery | MCP distribution/hosting channel; useful partner for creator-side payments. | https://smithery.ai |
| MCP Marketplace creators | Marketplace already sells discovery/payments for MCP creators. | https://mcp-marketplace.io/for-creators |
| MCPlug | Agent skill marketplace audience overlaps with paid tools. | https://mcplug.store |
| LastMile AI mcp-agent | MCP-native agent framework; useful demo partner for paid tool invocation. | https://github.com/lastmile-ai/mcp-agent |
| CrewAI | Agent framework audience can validate whether Lightning-paid tools are ergonomically usable. | https://github.com/crewAIInc/crewAI |
| LangGraph / LangChain MCP users | Tool-calling framework audience; good for SDK/adapter feedback. | https://github.com/langchain-ai/langgraph |
| webcrawl-mcp | Local-first crawler with optional paid API fallback angle. | https://github.com/andyliszewski/webcrawl-mcp |
| webclaw | Self-hosted scraping server with cloud/API ambitions; good independent creator lead. | https://github.com/0xMassi/webclaw |
| GitMCP | Remote MCP server for repo docs; could test small paid documentation lookups. | https://gitmcp.io |
| Agent-MCP | Multi-agent orchestration project; could validate agent-to-tool payment UX. | https://github.com/rinadelph/Agent-MCP |

## Next Validation Move

1. Deploy the waitlist form and backend.
2. Generate a fresh MCP Lightning invoice in production.
3. Pay it manually from a real wallet.
4. Confirm JWT issuance.
5. Run `usage` and `charge`.
6. Send direct outreach to the first five targets with the paid-call proof attached.

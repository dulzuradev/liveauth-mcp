# PermitSignal

PermitSignal is paid construction-permit intelligence for AI agents. It imports public
municipal permit records, normalizes them into one query model, infers deterministic work
categories, scores explainable trade opportunities, and exposes the results through MCP.
Successful valuable tool calls use LiveAuth's existing MCP Gate budget, fee accounting,
revenue events, webhooks, analytics, and signed receipts.

PermitSignal aggregates and interprets public records. It is not the official source of
any permit, and every returned project includes its source identifier, source record ID,
record URL when available, and last source-update timestamp.

## Architecture

```text
MCP client (stdio bridge or HTTP MCP)
  -> LiveAuth public project key + MCP Gate JWT
  -> PermitSignal JSON-RPC controller
  -> input validation and per-agent rate limit
  -> normalized SQLite query and deterministic analysis
  -> successful result only
  -> existing LiveAuth MCP metering
       MCP gate token / L402 balance
       tool price and daily budget
       fee split and revenue event
       signed receipt and webhook
  -> structured MCP result with provenance and receipt metadata

Background worker
  -> fixed official Socrata endpoints
  -> incremental cursor + bounded paging + retry
  -> source-specific mapping
  -> deterministic category inference
  -> idempotent normalized upsert
  -> source health and sync state
```

The business logic is in scoped services and is not coupled to the MCP transport. The
stdio example forwards MCP calls to the native authenticated HTTP MCP endpoint; it does
not duplicate query or billing logic.

## Supported municipalities

| Municipality | Official dataset | Adapter cursor | Notes |
| --- | --- | --- | --- |
| Austin, TX | [Issued Construction Permits](https://data.austintexas.gov/Building-and-Development/Issued-Construction-Permits/3syk-w9eu) | `statusdate` | Includes building, electrical, mechanical, plumbing, value, contractor, occupancy, and official record links. |
| San Francisco, CA | [Building Permits](https://data.sfgov.org/Housing-and-Buildings/Building-Permits/i98e-djp9) | `data_loaded_at` | Nightly/current DBI data. `record_id` is retained because a permit number can occur at multiple addresses. |
| Seattle, WA | [Building Permits](https://data.seattle.gov/Permitting/Building-Permits/76t5-zqzr) | `issueddate` | Includes explicit residential/non-residential mapping, cost, status, contractor, coordinates, and record links. |

All adapters use fixed HTTPS resource URLs. MCP input can never select an upstream URL.
Adding a city requires implementing `IPermitSourceAdapter`; core queries do not know the
municipal schema.

## Normalized data

The main entities are:

- `PermitSource`: official dataset identity, city/state, adapter, health, last sync, error.
- `PermitSyncState`: cursor, continuation offset, attempts, failures, processed count.
- `PermitProject`: normalized project/permit fields and public source provenance.
- `PermitProjectCategory`: the many deterministic work categories inferred for a permit.

The unique key is `(PermitSourceId, SourceRecordId)`, so a repeated sync updates a record
instead of duplicating it. Indexed filters include issue date, municipality/state, value,
permit type, occupancy, contractor, normalized address, and category.

Missing upstream values remain `null`. PermitSignal does not manufacture owners,
contractor licenses, coordinates, value, or dates. No private contact enrichment or
data-broker information is used.

## Work categories and scoring

The initial classifier uses isolated deterministic rules for:

`GeneralConstruction`, `Roofing`, `HVAC`, `Electrical`, `Plumbing`, `Solar`,
`FireProtection`, `Mechanical`, `Structural`, `Demolition`, `NewConstruction`,
`Renovation`, `TenantImprovement`, and `Other`.

Opportunity scores are additive, configuration-driven, clamped to 0-100, and returned
with every awarded reason. Defaults include:

| Signal | Points |
| --- | ---: |
| Issued within 3 days | 20 |
| Issued within 7 days | 15 |
| Commercial | 15 |
| Value at least 1,000,000 | 25 |
| Value at least 250,000 | 15 |
| Strong trade match | 25 |
| Weak trade match | 10 |
| New construction | 15 |

Levels are Low (0-39), Medium (40-69), and High (70-100). Basic search and scoring do
not call an LLM.

## MCP tools

The HTTP MCP endpoint is `POST /api/permitsignal/mcp` and supports `initialize`, `ping`,
`tools/list`, and `tools/call` using JSON-RPC 2.0. It currently advertises MCP protocol
version `2025-06-18`.

### `search_projects` — default 5 sats

Filters: `location`, `municipality`, `state`, `issued_after`, `issued_before`,
`minimum_project_value`, `maximum_project_value`, `permit_type`, `work_category`,
`commercial_only`, `residential_only`, `keywords`, `contractor_name`, and `limit`.
The maximum limit is 100.

```json
{
  "location": "Austin, TX",
  "issued_after": "2026-08-01",
  "minimum_project_value": 250000,
  "work_category": "HVAC",
  "commercial_only": true,
  "limit": 25
}
```

### `find_opportunities` — default 10 sats

Filters projects, normalizes the requested trade, and applies explainable scoring. It
returns the project, score, level, matched trade, match strength, reasons, project value,
permit age, categories, and source.

```json
{
  "location": "Austin, TX",
  "trade": "Electrical",
  "issued_within_days": 7,
  "minimum_project_value": 100000,
  "commercial_only": true,
  "limit": 25
}
```

### `analyze_project` — default 15 sats

Accepts a PermitSignal GUID, official source record ID, or permit number. Returns project
summary, scope, stage, age, likely trades, supplier/service opportunities, signals, and
source records.

```json
{ "project_id": "20000000-0000-0000-0000-000000000003" }
```

### `property_history` — default 20 sats

Uses an exact normalized address match and does not guess across low-confidence addresses.
Returns first/most recent permit date, total known value, common categories, major projects,
and oldest-to-newest chronological records.

```json
{
  "address": "760 14th Street, Apt 2",
  "municipality": "San Francisco",
  "state": "CA",
  "limit": 50
}
```

Tool results contain ordinary MCP text content, `structuredContent`, and `_meta.liveauth`:

```json
{
  "content": [{ "type": "text", "text": "{...}" }],
  "structuredContent": { "count": 1, "projects": [] },
  "isError": false,
  "_meta": {
    "liveauth": {
      "paid": true,
      "priceSats": 5,
      "revenueEventId": "...",
      "receipt": { "version": "mcp-call-receipt-v1", "payload": "...", "signature": "..." },
      "callsUsed": 1,
      "satsUsed": 5
    }
  }
}
```

## LiveAuth Meter integration

PermitSignal reuses these LiveAuth components:

- MCP Gate PoW, Lightning, or L402-bundle authentication and its signed JWT.
- `McpGateToken` daily budget and usage counters, or the project's L402 balance.
- registered `McpTool` prices and existing platform fee calculation.
- `McpToolRevenueEvent` analytics, including denied charges.
- `McpReceiptService` HMAC-signed `mcp-call-receipt-v1` receipts.
- existing paid-tool webhooks and admin MCP revenue analytics.

Execution is intentionally ordered as validate -> query/analyze -> charge -> return. If
validation or an internal tool execution fails, metering is never invoked. If the result
succeeds but the budget is unavailable, the result is withheld and a denied event is
recorded. An optional `X-LiveAuth-Idempotency-Key` makes client retries charge once per
MCP gate token.

Prices are not accepted from MCP callers. Configure them at startup:

```json
{
  "PermitSignal": {
    "Tools": {
      "SearchProjects": { "PriceSats": 5 },
      "FindOpportunities": { "PriceSats": 10 },
      "AnalyzeProject": { "PriceSats": 15 },
      "PropertyHistory": { "PriceSats": 20 }
    }
  }
}
```

The four first-party tool registrations are created or updated from this configuration
during startup.

## Running locally

The development configuration seeds five deterministic examples: commercial HVAC,
residential reroofing, commercial electrical service upgrade, new commercial construction,
and commercial plumbing renovation. Automatic external synchronization is off by default,
so local development does not depend on city APIs.

```bash
cd LiveAuthCore
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5166 \
ConnectionStrings__Default='Data Source=liveauth.db' \
LiveAuth__PowHmacSecret='dev-only-change-me-32-or-more-characters' \
LiveAuth__DemoProjectId='00000000-0000-0000-0000-000000000002' \
Jwt__SigningKey='dev-only-jwt-signing-key-32-characters' \
Lnd__UseMock=true \
dotnet run --no-launch-profile
```

Startup uses `EnsureCreated` for a new database and the idempotent table migration in
`V20260807_AddPermitSignal.sql`/`PipelineExtensions` for an existing LiveAuth SQLite file.

To enable real-source imports:

```bash
export PermitSignal__Sync__Enabled=true
export PermitSignal__Sync__InitialLookbackDays=30
export PermitSignal__Sync__IntervalMinutes=60
```

The LiveAuth admin app exposes a **PermitSignal** page for record counts, 24-hour additions,
source health/errors, last sync, MCP calls/sats by tool, top municipalities, and bounded
per-source or all-source sync controls. The backing endpoints are
`GET /api/admin/permitsignal` and
`POST /api/admin/permitsignal/sync?source=austin-issued-construction-permits`.

## Testing a paid MCP call

Install the stdio bridge once:

```bash
cd examples/permitsignal-mcp
npm install
cp .env.example .env
```

Set the local API URL and public project key in `.env`. In TEST mode, obtain a short-lived
MCP JWT without a production Lightning payment:

```bash
npm run auth:test
```

Copy the printed `export LIVEAUTH_JWT='...'` command into the shell, then run:

```bash
npm start
```

A desktop MCP client can launch the same bridge:

```json
{
  "mcpServers": {
    "permitsignal": {
      "command": "node",
      "args": ["/absolute/path/to/LiveAuth/examples/permitsignal-mcp/server.mjs"],
      "env": {
        "LIVEAUTH_API_URL": "http://127.0.0.1:5166",
        "LIVEAUTH_API_KEY": "la_pk_your_project_public_key",
        "LIVEAUTH_JWT": "your_short_lived_mcp_jwt"
      }
    }
  }
}
```

For direct HTTP testing after authentication:

```bash
curl -sS http://127.0.0.1:5166/api/permitsignal/mcp \
  -H 'Content-Type: application/json' \
  -H "X-LW-Public: $LIVEAUTH_API_KEY" \
  -H "Authorization: Bearer $LIVEAUTH_JWT" \
  -H 'X-LiveAuth-Idempotency-Key: demo-electrical-search-1' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"find_opportunities","arguments":{"location":"Austin, TX","trade":"Electrical","issued_within_days":30,"minimum_project_value":100000,"commercial_only":true}}}'
```

Run automated tests:

```bash
dotnet test LiveAuthCore.Tests/LiveAuthCore.Tests.csproj --filter FullyQualifiedName~PermitSignal
```

The focused suite covers source mappings, category inference, address normalization,
scoring explanations, database filters, duplicate prevention, incremental updates,
configuration prices, idempotent charging, budget denial, failure-without-charge, and an
end-to-end MCP discovery/search/receipt flow.

## Adding a municipality

1. Confirm an official machine-readable source and its reuse terms.
2. Implement `IPermitSourceAdapter`, normally by deriving from `SocrataPermitAdapter`.
3. Hard-code the official HTTPS resource URL and choose a reliable incremental field.
4. Map only source-provided values into `NormalizedPermitRecord`; keep missing values null.
5. Register the typed HTTP client and adapter in `ServiceCollectionExtensions`.
6. Add representative source-schema and repeat-sync tests.
7. Add the official dataset and cursor limitations to this document.

## Deployment

- Apply the included idempotent schema migration before or during application startup.
- Keep sync page count, page size, lookback, and interval bounded per source rate limits.
- Configure the PermitSignal MCP tool owner project with `PermitSignal__ProjectId` if it
  should differ from `LiveAuth__DemoProjectId`.
- Use normal LiveAuth production Lightning/L402 configuration. Do not enable mock LND in
  Production; LiveAuth's existing production safety validation rejects it.
- Monitor `/api/admin/permitsignal`, existing MCP revenue analytics, structured sync logs,
  and paid-tool webhooks.

## Known limitations

- The existing LiveAuth branch is SQLite-backed, so this MVP follows that infrastructure;
  a future PostgreSQL deployment should add a provider-specific EF migration and use the
  same entities/indexes.
- Seattle's public dataset lacks a dedicated load/update timestamp; its incremental cursor
  is `issueddate`, so corrections to old permits can be delayed until a deliberate lookback
  or backfill.
- San Francisco residential/commercial classification is deterministically inferred from
  public use text because the dataset does not expose Austin/Seattle's explicit mapped field.
- Addresses use conservative exact normalization, not parcel/geocoder identity resolution.
- Category inference and opportunity scoring are rules, not predictions or guarantees.
- Initial sync is intentionally bounded. Historic backfills need repeated/manual runs or a
  separate controlled backfill configuration.
- The admin page is deliberately operational and compact; it is not a customer-facing
  lead-management dashboard.

## Recommended next improvements after real usage

1. Measure which filters and explanations correlate with retained paid usage, then tune
   categories and scoring weights from observed outcomes rather than adding speculative ML.
2. Add parcel identifiers and an explicit high-confidence geocoding/address-canonicalization
   pipeline to improve property history without fuzzy false matches.
3. Add PostgreSQL/provider migrations and a controlled historic-backfill job once record
   volume or query concurrency makes SQLite the limiting factor.

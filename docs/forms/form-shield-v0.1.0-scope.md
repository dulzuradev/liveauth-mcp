---
title: "Form Shield v0.1.0 — Scope (PARKED 2026-06-11)"
description: "Thin PoW drop-in for HTML forms. Scoped, parked, and not being built this week. Documenting so the work isn't lost."
date: 2026-06-11
author: Sydney
status: parked
---

# Form Shield v0.1.0 — Scope

> **Status: parked.** This spec is captured here so the work isn't lost. We are not building this in the 2026-06-11 → 2026-06-13 window. Revived only if a 1-day window opens and the Tier 1 marketing work is done.

## Position

**Not** "LiveAuth vs Turnstile" or "LiveAuth vs hCaptcha" or "LiveAuth vs Friendly Captcha." That lane is closed: Cloudflare Turnstile ships invisible + PoW + non-Cloudflare-CDN + analytics + trust distribution. We lose that head-to-head.

The lane is: **"bot-cost middleware for the long tail of HTML forms."** Indie SaaS, small publishers, hobby projects, internal tools. The buyer has forms, no engineering team, and a real signup-spam problem. The pitch is: drop one script, all forms on the page get PoW-gated, spam stops being free.

PoW-first is the differentiated philosophy: transparent, privacy-friendly, compute-cost based. No tracking pixel, no behavior fingerprinting, no Google reCAPTCHA monopoly on "are you human?".

## The freemium ladder (the actual strategic value)

This is not the bet. The bet is paid MCP tools. Form Shield is a **feeder product**:

```
Form Shield free         → indie SaaS installs (1 line of script)
   ↓ hits a limit        → e.g. >1,000 submissions/mo
Form Shield Pro $5/mo    → hosted proxy mode, dashboard, alerts
   ↓ needs more          → e.g. wants to charge for tool calls
Paid API / MCP metering  → real LiveAuth customer
```

**Success metric is not "10k Form Shield installs."** It's *"3% of Form Shield installs graduate to paid API/MCP within 12 months."* If we hit 10k installs and 0% graduation, Form Shield is a vanity project, not a feeder. The metric we report is graduation rate, not install count.

## v0.1.0 — Ship when there's a 1-day window

| | v0.1.0 (1 day) | v0.2.0 (later) | Never |
|---|---|---|---|
| Auto-detect `<form>` | ✓ | | |
| PoW in Web Worker, JWT in hidden field | ✓ | | |
| Hosted proxy mode (`/v/{project}/submit`) | ✓ | | |
| Server-side verification snippets (Node/PHP/Rails/Laravel) | | ✓ | |
| Dashboard (submissions/blocks/solve time/origins) | | ✓ | |
| React/Vue/Angular adapter | | | ✗ |
| "Replace Turnstile" marketing | | | ✗ |
| Multi-form per page, SPA routing | | ✓ | |
| Custom challenge difficulty | | | ✗ |

**Hard scope cuts:**
- Plain `<form method="post">` only. No SPAs, no React/Vue/Angular routing, no AJAX, no Webflow/Shopify/WordPress plugins, no CSP dance, no ad-blocker workaround.
- No visual challenge, ever. The whole product is invisible.
- No custom branding, no per-site config beyond `data-site` and `data-api`.

**One promise, sharp:** *"Make spam and scripted form abuse cost compute with one script."* Modest. Bounded. Defensible.

## Security theater mitigation (the killer risk)

The single biggest risk: customers install the script, see the JWT, and skip backend verification. Then a 12-year-old with `curl` posts to their endpoint unverified and they think they're protected. The product becomes security theater.

**Three layers of mitigation, in priority order:**

1. **Hosted proxy mode is the answer.** Form posts to `https://liveauth.app/v/{project}/submit` instead of the customer's backend. LiveAuth verifies the JWT, then forwards the cleaned payload to the customer's actual `action` URL. **Zero server-side code required to be safe.** This is the path most customers will take and it should be the default in the dashboard onboarding.
2. **Copy-paste server snippets.** For the customers who *want* their own backend, ship 12-line snippets per stack (Node, PHP, Rails, Laravel, Go) that do JWT verification with `jose` / `firebase-php-jwt` / etc. The dashboard auto-detects backend stack from the `Server` header and serves the right snippet.
3. **The hidden field name should be discoverable.** Don't call it `liveauth_token` — call it something obvious like `liveauth_pow_token_v1` so anyone reading the form spec immediately knows what it is and what to do with it.

## Pricing

- **Free:** 1,000 submissions/mo per project. Email alerts above 500/mo.
- **Form Shield Pro: $5/mo.** Unlimited submissions, hosted proxy mode, dashboard.
- **Paid API / MCP metering:** $29/mo (current LiveAuth standard tier).

The $5 Form Shield Pro tier is the freemium ladder's first paid step. $5 is below the threshold of "submit an expense report" — the customer doesn't have to ask anyone. That's the point.

## Technical shape (what v0.1.0 looks like in code)

- **Client:** `embed.js` (8.7KB, no dependencies, inlined Web Worker). Already written and tested as of 2026-06-11; lives in `~/.Trash/liveauth-embed/` (recoverable) and was briefly on `feature/embed-widget-integration` in the LiveAuth monorepo (branch deleted).
- **Server:** New `LiveAuthCore/Controllers/PublicFormShieldController.cs` exposing `POST /v/{project}/submit` and the verification endpoint. Should reuse the existing PoW challenge/verify endpoints.
- **Hosted proxy:** Caddy route `/v/{project}/submit/*` → LiveAuthCore → forward to customer's `action` URL. Need to handle CORS preflight carefully because this is cross-origin by design.
- **Dashboard:** Defer to v0.2.0. v0.1.0 ships without it; emails handle the alerts.

## Why we're not building this right now

1. **The 06-11 memo's "no new picks until shipped" rule is now bypassed by abandonment, not by shipping.** The discipline broke. Re-adding Form Shield as a 4-hour side-project today is exactly the "one more thing" that breaks Tier 1 focus.
2. **Tier 1 work is the priority** — mcp.so listing, blog post, Lightning Labs spec submission, mcpservers.org listing. These are unblocking the main bet (paid MCP tools).
3. **The code is recoverable.** `~/.Trash/liveauth-embed/` has the source. The test suite passes. The algorithm is validated. None of the work is lost; it's just not on the right priority lane.
4. **Revisit Monday** if there's a 1-day window and the Tier 1 work is done. Otherwise, defer until the freemium ladder story needs a feeder product.

## What to NOT do when this gets revived

- ❌ Don't rebrand the embed widget as "LiveAuth Shield" or "LiveAuth Forms" or any "LiveAuth" name. The whole point is it's a separate feeder product with its own positioning. "Form Shield" is a working name; treat the branding as a separate problem.
- ❌ Don't include the @liveauth-labs/sdk package as a dependency. It's a billing SDK for the dashboard, not a form embed. They serve different buyers. The embed should be its own npm package, name TBD.
- ❌ Don't try to "win against Turnstile" in any marketing copy. The product promise stays modest: "spam costs compute with one script."
- ❌ Don't ship dashboard / analytics / SPA support in v0.1.0. The 1-day scope is the entire discipline. The hard scope cuts in the table above are not negotiable.

## Decision log

- **2026-06-11 14:00 PT** — original "embed widget v0.1.0" abandoned. Code in trash.
- **2026-06-11 20:43 PT** — Scott reframes as "Form Shield" supporting product, accepts the lane. Agrees to park, not build this week.
- **2026-06-11 20:48 PT** — this doc written. Code recoverable from trash. Spec captured here so it doesn't get lost.

— Sydney

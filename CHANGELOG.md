# Changelog

## 1.2.0 — Unreleased

- Add `ChargeDeniedError` with reason/code and tool identity. It remains a
  `BudgetExceededError` subclass for existing catch handlers.
- Preserve structured HTTP denials in `gate.charge()`; distinguish availability,
  budget, rate-limit, and generic denials in `invoke()`/`gateTool()`.
- Add `ToolExecutionError` for synchronous/asynchronous handler failures after a
  successful charge, retaining charge, retry key, and non-enumerable original cause.
- Document billable execution attempts, tool lifecycle, and the three ID meanings.
- Preserve successful return values, receipt field names, and charge-before-execute.

Minor release: new public error classes and handler-error wrapping are material
API additions. Consumers matching the original handler error should inspect
`ToolExecutionError.cause`; consumers matching budget errors should inspect reason.
Requires backend rollout for structured unknown-tool and Draft-specific denials.

Release from the reviewed checkout (not performed by Codex):

```sh
cd /Users/scott/Repos/liveauth-mcp
npm ci
npm test
npm run build
npm pack --dry-run
npm publish --access public
```

After publication update InvokeWorks to ^1.2.0, regenerate its lockfile, run its
tests/typechecks/builds, and redeploy. Backend deployment is a separate rollout.

CHANGELOG

Unreleased
- (placeholder)

## 0.1.0-alpha.6 – 2025-09-21
- Fix: Sanitizes API keys (remove CR/LF) at persistence and provider construction to prevent HTTP header newline exception during `/setup`.
- Fix: Adjusted `LLMProviderFactory` to emit provider-specific not-configured messages matching tests.
- Improvement: Suppressed verbose Anthropic provider info/debug logs by default; configurable via `CODEPUNK_PROVIDER_LOGLEVEL` env var.
- Internal: Added consistent sanitation utility points (`AuthFileStore`, providers, bootstrap) to reduce future header value issues.

## v1.2.3 – 2025-09-14
- Feature: Persist `PlanSummary` (source, goal, candidateFiles, rationale, message counts, truncated, token usage heuristic) for `plan create --from-session`.
- Feature: JSON output now includes `tokenUsageApprox { sampleChars, approxTokens }` in `plan.create.fromSession.v1`.
- Improvement: Added rationale & token usage persistence groundwork for future AI plan generation.
- Change: `ISessionSummarizer.SummarizeAsync` now returns nullable `SessionSummary?` for graceful failure.
- Tests: Added summary persistence, token usage math, and backward compatibility tests; removed duplicate legacy test file.
- Docs: Updated `PARITY_PLAN.md` (v1.2.3) and `SPEC_PLAN_FROM_SESSION.md` with new fields.

## Earlier
- Feature: `plan create --from-session` emits JSON using schema `plan.create.fromSession.v1`.
- Improvement: Added `CODEPUNK_QUIET` / `--json` handling across console commands to suppress decorative output for automation.
- Tests: Added component tests validating quiet-mode and summarizer-fallback behavior.

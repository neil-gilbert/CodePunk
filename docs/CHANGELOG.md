CHANGELOG

Unreleased
- (placeholder)

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

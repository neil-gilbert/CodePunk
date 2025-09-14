CHANGELOG

Unreleased
- Feature: `plan create --from-session` emits JSON using schema `plan.create.fromSession.v1`.
- Improvement: Added `CODEPUNK_QUIET` / `--json` handling across console commands to suppress decorative output for automation.
- Change: `ISessionSummarizer.SummarizeAsync` may now return `null` to indicate no summary could be produced.
- Tests: Added component tests validating quiet-mode and summarizer-fallback behavior.

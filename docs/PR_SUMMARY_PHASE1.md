PR Summary — Phase 1: Plan-from-Session + Quiet/JSON Mode

Summary
- Adds `plan create --from-session` JSON schema emission `plan.create.fromSession.v1` and deterministic session summarization support.
- Propagates quiet-mode gating so automation can request a single clean JSON payload via `--json` or `CODEPUNK_QUIET`.
- Introduces graceful fallback semantics when a summarizer isn't available or cannot infer a summary.

Key Changes
- CLI: `plan create --from-session` now emits machine-readable JSON using schema `plan.create.fromSession.v1`.
- Rendering: `Rendering.OutputContext.IsQuiet()` is used across command modules to suppress decorative output (tables, panels, markup) when quiet-mode is active.
- Interface: `ISessionSummarizer.SummarizeAsync(...)` now returns `Task<SessionSummary?>`. A `null` return indicates the summarizer could not infer a summary.
- Tests: Component tests added to validate quiet-mode and summarizer-fallback behaviors.

Backward Compatibility
- Human-facing behavior is unchanged when quiet mode is not enabled.
- Automation: callers that relied on previously-printed decorative output should use `--json` or set `CODEPUNK_QUIET=1` to receive only the JSON payload.

Files Touched (high-level)
- `src/CodePunk.Console/Commands/Modules/*` — added quiet gating in `Plan`, `Run`, `Sessions`, `Models`, `Agent`, `Auth` modules.
- `src/CodePunk.Core/Abstractions/ISessionSummarizer.cs` — return type became nullable `SessionSummary?`.
- `tests/CodePunk.Console.Tests/Commands/*` — added tests for summary-unavailable and quiet+fallback cases.

Notes for reviewers
- Focus review on any unguarded console writes that may still appear before JSON emission. Use `CODEPUNK_QUIET=1` and run `plan create --from-session --json` to verify a single JSON object is output.
- The change to `ISessionSummarizer` is intentionally nullable to allow safe fallbacks in environments without a summarizer implementation registered.

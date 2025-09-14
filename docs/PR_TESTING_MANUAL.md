Manual Testing - Phase 1: Plan-from-Session and Quiet/JSON Mode

Prerequisites
- .NET 9 SDK installed and available on PATH.
- From repository root: run commands in this guide using `zsh`.

Quick verification steps

1) Build and run the console test project

```bash
dotnet test tests/CodePunk.Console.Tests/CodePunk.Console.Tests.csproj -v minimal
```

2) Verify quiet JSON output for `plan create --from-session`

- Create a plan from an existing session in JSON mode and confirm the output is a single JSON object.

```bash
# Example: request a JSON-only response
CODEPUNK_QUIET=1 dotnet run --project src/CodePunk.Console -- plan create --from-session --session <session-id> --json

# Or use the flag directly (same effect)
dotnet run --project src/CodePunk.Console -- plan create --from-session --session <session-id> --json
```

- Expected: the process prints exactly one JSON object (no leading markup or tables). The top-level object will include `schema: "plan.create.fromSession.v1"` and either a `plan` payload or an `error` object with `code` set to `SummaryUnavailable` or `SummarizerUnavailable`.

3) Test when no summarizer is registered (fallback)

- To simulate an environment where no `ISessionSummarizer` is available, run the command in a test host that does not register the summarizer or use the supplied component tests.

4) Manual inspection for noisy output

- Run the same command without `--json` to review human-facing text:

```bash
dotnet run --project src/CodePunk.Console -- plan create --from-session --session <session-id>
```

- Expected: human-readable tables/panels/markup are present. When `CODEPUNK_QUIET=1` is set, those decorations should be suppressed.

Diagnostics

- If you observe non-JSON output prior to the JSON object when using `--json` or `CODEPUNK_QUIET=1`, search for unguarded `MarkupLine` or `console.Write` calls in `src/CodePunk.Console/Commands/Modules` and add `if (!Rendering.OutputContext.IsQuiet())` guards.

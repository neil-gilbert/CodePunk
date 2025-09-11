## Title
Add sessions CLI (list/show/load), models command enhancements, and persistence tests

## Summary
This PR implements Item 2 of the release plan: session persistence surfacing via a first-class `sessions` root CLI command set. It also enhances the `models` command and expands the automated test suite.

## Key Changes
- Added `sessions` root command with subcommands:
  - `sessions list` (supports `--take <n>` and `--json` for machine-readable output)
  - `sessions show --id <sessionId>` (transcript display or JSON)
  - `sessions load --id <sessionId>` (activates a session context; user feedback rendered)
- Added JSON output and `--available-only` filtering to `models` command.
- Integrated session ActivitySource spans (`sessions.list`, `sessions.show`, `sessions.load`).
- Spectre.Console table rendering for session listings (with graceful empty state).
- Updated README to document new commands and current test counts.
- Added comprehensive CLI scenario tests for sessions (empty, create/list with limit, show existing & missing, load existing & missing).

## Rationale
Surfacing session management outside the interactive loop enables scripting, automation, and CI integration (e.g., exporting most recent session ID). JSON output options lay groundwork for future tooling (e.g., piping through jq).

## Test Coverage
New tests: `SessionsCommandTests` (7). Total test summary now: 135 total (134 passing, 1 skipped integration). Sessions scenarios validated deterministically with isolated temp config homes.

## Backward Compatibility
No breaking changes to existing commands. Interactive slash commands unchanged. Root command now includes `sessions` and enhanced `models` behavior; previous placeholder models output replaced by richer content (still compatible for human consumption).

## Telemetry
Adds three new activity spans for observability around session operations.

## Follow Ups / Future Work
- Consider non-zero exit codes for not-found cases (currently logs message and returns 0 for simplicity).
- Output capture helper to assert Spectre markup (reduce reliance on manual visual validation).
- Potential `sessions delete` and `sessions export` in future iteration.
- Provide `--format jsonl` for streaming transcript export.

## Screenshots / Output Samples
List (truncated):
```
sessions list --take 3
Id                 Title       Agent  Model     Msgs  Updated
20250911070659...  Session 4   -      (default) 0     2025-09-11 07:06:59Z
...
```
Show:
```
sessions show --id 20250911070320-1a13f2
Title: Demo
Id: 20250911070320-1a13f2
Messages: 2
[user] Hello
[assistant] Hi there
```

## Checklist
- [x] Sessions CLI implemented
- [x] Models command updated
- [x] Tests added & passing
- [x] README updated
- [x] Telemetry spans added

## License
All contributions remain under MIT.

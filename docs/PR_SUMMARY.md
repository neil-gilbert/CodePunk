# PR: MVP Coder CLI (v1.2.1)

## Overview
Delivers the focused coder workflow: authenticate providers, define agents, run one‑shot or session‑based prompts, persist and inspect sessions, generate structured multi‑file change plans (including deletions), review diffs, and apply changes safely with backups. Adds optional models catalog. All key commands emit stable, versioned JSON schemas for scripting.

## Implemented Features
- **Run**: One-shot or continue existing sessions with JSON (`run.execute.v1`) including approximate token usage (char/4 heuristic).
- **Agents**: Create/list/show/delete agent definitions with persisted prompts.
- **Auth**: Store/list/remove provider API keys (file-based secure store).
- **Sessions**: Persistent storage & retrieval with JSON (`sessions.list.v1`, `sessions.show.v1`).
- **Planning**: create/add/diff/show/list/apply with drift detection, dry-run, force override, deletion staging, per-file unified diffs, backups. JSON schemas: `plan.create.v1`, `plan.add.v1`, `plan.list.v1`, `plan.show.v1`, `plan.diff.v1`, `plan.apply.v1`.
- **Deletion Actions**: applied, dry-run, deleted, dry-run-delete, skip-missing, skipped-drift, skipped-error, delete-error.
- **Models**: Table + JSON (`models.list.v1`) listing provider/model entries and key presence (`hasKey`).
- **Unified JSON Output**: Central helper & schema constants file for consistent machine-readable responses.
- **Token Usage**: Approximate counts in run JSON (extension to other commands deferred).
- **Test Resilience**: ANSI escape stripping & robust last-object JSON extraction in console tests.

## Stability & Quality Improvements
- Schema constants prevent drift; all commands reference a single definitions file.
- Sessions show/load not-found exit code normalized (non‑error for scripting while still emitting error message).
- Models command emits JSON while preserving line-oriented rows for legacy tests.
- Plan & models tests hardened to ignore surrounding UI / panels.
- Public `PlanFileStore.Sha256` enables consistent hashing in tests & tooling.

## Documentation & Versioning
- README: End-to-end coder loop walkthrough + JSON examples (including deletion scenarios).
- Parity Plan: Updated to v1.2.1 capturing models command + test stabilization.

## Data & Storage Layout
- User config: `~/.config/codepunk` (auth, agents, sessions).
- Repo-local: `.codepunk/plans`, backups per apply under timestamped directory.

## Telemetry (Minimal)
- Light activity spans (plan.*, run) placeholder; extended OpenTelemetry deferred.

## Implemented JSON Schemas
`run.execute.v1`
`sessions.list.v1` / `sessions.show.v1`
`plan.create.v1` / `plan.add.v1` / `plan.list.v1` / `plan.show.v1` / `plan.diff.v1` / `plan.apply.v1`
`models.list.v1`

## Deferred (Post-MVP)
- Semantic index & search
- AI-driven plan creation (current create is manual goal)
- Token usage extension beyond run
- Front-matter parser / advanced prompt layering
- Serve mode / TUI / plugin system / CI automation & GitHub integration
- Advanced security scanning & refactor tooling

## Risk & Mitigation Highlights
- **Drift**: SHA-256 hash comparison before apply; skip or force with `--force` flag.
- **Safety**: Backups of originals (including deletions) under plan-specific directory.
- **JSON Stability**: Tests isolate JSON from UI noise; schema field required for contract identification.
- **Token Accuracy**: Approx heuristic until provider-level accounting integrated.

## Testing Summary
- Unit tests for command JSON shapes & planning actions (including deletion & drift).
- Integration workflow (scaffold) validating coder loop sequence.
- Console tests robust against stylistic Spectre output changes.

## Follow-Up Ideas
- `plan create --from-session` to seed goals from recent messages.
- Expand token/cost tracking inside tool loop & streaming path.
- Shell-level E2E harness for regression across OS environments.

## Definition of Done (Met)
1. Core commands operational with versioned JSON schemas.
2. Deletion + drift detection implemented & tested.
3. Run command exposes approximate token usage.
4. Documentation (README + parity plan) updated.
5. Tests green (unit + console + integration scaffold).

---
Prepared for merge: v1.2.1
# feat: Plan workflow (create/add/diff/apply) with drift detection & backups (CP-4)

## Summary
Introduces a full "plan" workflow to safely stage, review, and apply multi-file changes via the CLI. Adds staging (with before/after content capture), diff inspection, drift detection, dry-run simulation, force override, and automatic per-apply backups. Documentation updated and tests added to validate core behaviors.

## Motivation
Provides a structured, auditable path for applying code or text modifications—especially useful for AI-assisted or batch refactors—while minimizing risk through deterministic hashing, drift checks, and backups.

## New CLI Commands
```
plan create <name>              # Create a new plan record
plan add <planId> <file>        # Stage a file (captures before & optional after)
plan list                       # List existing plans
plan show <planId>              # Show plan metadata & staged files
plan diff <planId>              # Render unified diffs for staged changes
plan apply <planId> [--dry-run] [--force]
```

## Core Behaviors
- Staging:
  - Stores original (before) content hash + optional after content
  - Generates naive unified diff (line-based)
  - Computes SHA256 for before/after to detect drift
- Diff Viewing:
  - `plan diff` renders readable unified diffs (ANSI formatting)
- Apply:
  - Validates current file hash vs. stored `HashBefore`
  - Skips drifted files unless `--force` supplied
  - `--dry-run` simulates without modifying files or writing backups
  - Reports counts: applied / skipped (no after) / drifted
- Backups:
  - For each non-dry-run apply, originals are copied to: `plans/backups/<planId>-<timestamp>/`

## Safety Features
| Feature | Purpose |
|---------|---------|
| Hash-based drift detection | Prevents unintended overwrites when file changed after staging |
| Backups per apply run | Enables manual recovery |
| Dry-run mode | Preview impact without file writes |
| Force flag | Explicit override for known drift |

## File System Layout
```
<config-root>/
  plans/
    index.json                # Registry of plan records
    <planId>.json             # Individual plan definition
    backups/
      <planId>-<timestamp>/   # Original files before apply
```

## Tests Added / Updated
- Plan staging diff generation
- Apply dry-run (no file writes, no backups)
- Drift detection (skip) vs. `--force` override
- Backup creation on apply (non-dry-run)
- No backup creation during dry-run

Current suite (post-change): 140 total / 139 passed / 1 skipped.

## Non-Functional Notes
- Diff builder is intentionally simple (future enhancement: context/hunk optimization)
- Backups currently flat per run (does not preserve directory hierarchy beyond filenames) – acceptable for MVP
- Exit code remains zero on drift unless escalated; potential future change

## Future Enhancements (Proposed Follow-Ups)
1. Improved diff algorithm (minimal hunks, configurable context)
2. Hierarchical backups mirroring original relative paths
3. Non-zero exit code when drift detected without `--force`
4. JSON output mode for automation (`plan add`, `plan apply`)
5. AI-assisted `plan generate` command
6. Normalize test method formatting for granular filtering

## Breaking Changes
None. New functionality is additive; existing commands unaffected.

## Manual Verification Steps
1. Create plan: `codepunk plan create sample`
2. Stage file: `codepunk plan add <id> README.md`
3. Edit file, restage with after content (or provide inline) and diff: `codepunk plan diff <id>`
4. Apply dry-run: `codepunk plan apply <id> --dry-run`
5. Apply real: `codepunk plan apply <id>` (check backups directory)
6. Modify original file externally, re-apply without force (observe drift skip), then with `--force`.

## Risk Assessment
Low. Changes are scoped to new command group + path config additions. Backups add a safety net; no changes to existing persistence formats outside addition of plan artifacts.

## Screenshots / Logs (Optional)
Not included; CLI output is deterministic and covered by tests.

## Checklist
- [x] Commands implemented
- [x] Backups added
- [x] Drift detection
- [x] Tests passing
- [x] README updated
- [x] PR description drafted

---
Generated for CP-4 feature branch. Ready for review.

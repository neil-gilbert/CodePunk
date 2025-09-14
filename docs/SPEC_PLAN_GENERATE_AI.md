# SPEC: plan generate --ai

Status: Draft (Phase 2)
Target Version: 1.3.0

## Purpose
Introduce AI-assisted multi-file plan generation that converts a high-level goal (or recent session context) into a structured change plan with proposed file edits/creations/deletions, rationales, and safety metadata. Builds atop existing plan storage, preserving backward compatibility.

## Command
```
codepunk plan generate --ai [--goal "text" | --from-session [--session <id>] [--messages N] [--include-tools]] \
                       [--model <modelId>] [--provider <provider>] \
                       [--max-files N] [--max-bytes N] [--allow-large] \
                       [--json]
```
Notes:
- Exactly one of `--goal` or `--from-session` required.
- If `--from-session` and no `--session`, use most recent session.
- Limits (`--max-files`, `--max-bytes`) defaulted from config; command-line overrides.
- `--allow-large` bypasses size rejection (adds safety flag).

## Flow
1. Resolve input goal:
   - From user text (`--goal`)
   - Or derive via existing Phase 1 summarizer (`--from-session`) producing seed goal + candidate files.
2. Collect file context sample (bounded): file paths + optional top N lines (exclude > size cap unless `--allow-large`).
3. Build prompt: system instructions + constraints + repository snapshot summary + (optional) session seed.
4. Invoke model provider (stream or single-shot) expecting structured JSON tool output.
5. Validate & sanitize:
   - Parse JSON, enforce schema.
   - Path safety: no abs/.. escapes; only inside repo.
   - Size caps; truncate large `afterContent` (flag).
   - Secret scan & redact; add safety flags.
6. Persist plan record with `PlanGeneration` metadata and proposed `PlanFileChange` entries (diff computed immediately).
7. Emit JSON / human output.

## JSON Schema: plan.generate.ai.v1
```jsonc
{
  "schema": "plan.generate.ai.v1",
  "planId": "...",
  "goal": "...",
  "provider": "openai",
  "model": "gpt-4.1-mini",
  "changeCount": 3,
  "files": [
    {
      "path": "src/Cache/CacheService.cs",
      "action": "modify",      // create|modify|delete
      "rationale": "Add in-memory layer",
      "truncated": false,
      "safetyFlags": [],
      "size": 1234
    }
  ],
  "tokenUsage": { "prompt": 1200, "completion": 800, "total": 2000 },
  "iterations": 1,
  "safetyFlags": ["SecretRedacted"],
  "truncated": false
}
```

## Generation Metadata (Plan Record additions)
```jsonc
"generation": {
  "provider": "openai",
  "model": "gpt-4.1-mini",
  "promptTokens": 1200,
  "completionTokens": 800,
  "totalTokens": 2000,
  "iterations": 1,
  "safetyFlags": ["SecretRedacted"],
  "refinedFromPlanId": null,
  "createdUtc": "2025-09-20T12:34:56Z"
}
```

## File Entry Extensions
Add optional properties to `PlanFileChange` (non-breaking):
- `Generated`: bool
- `Diagnostics`: string[] (e.g. ["TruncatedContent", "RedactedSecret"])

## Refinement (Stretch)
Command: `plan refine --id <planId> --goal "additional scope" [--json]`
Produces: `plan.refine.ai.v1` JSON; merges new changes (new plan or same plan?). Initial scope: create new plan referencing `refinedFromPlanId`.

## Safety & Limits
| Concern | Mitigation |
|---------|------------|
| Path traversal | Reject paths with `..` or starting `/` |
| Large file explosion | Enforce `--max-bytes` aggregate + per-file; truncate with flag |
| Secret leakage | Regex scan; replace with `<REDACTED>` + flag |
| Hallucinated deletions | Require existing file presence for delete actions |
| Oversized plan | Limit `--max-files`; fail with error code `TooManyFiles` |
| Invalid JSON | Retry parse up to 2 times; otherwise error `ModelOutputInvalid` |

## Error Codes
- `InputMissing`: neither goal nor from-session provided.
- `SessionNotFound`: session id not found.
- `ModelUnavailable`: provider/model not configured.
- `ModelOutputInvalid`: could not parse/validate model output.
- `TooManyFiles`: file count exceeds limit.
- `TooLarge`: size limit exceeded (aggregate) and not overridden.
- `UnsafePath`: rejected path outside workspace.

## Config Additions (.codepunk/config.json)
```jsonc
{
  "generation": {
    "maxFiles": 20,
    "maxBytes": 200000,
    "snippetLines": 40,
    "retryInvalidOutput": 2
  }
}
```

## Test Matrix
| Scenario | Expectation |
|----------|-------------|
| Simple create + modify | JSON emits correct action set, diffs stored |
| Delete of non-existent file | Rejected with error `UnsafePath` or validation fail |
| Excess files | Error `TooManyFiles` |
| Large file content | Truncated flagged `TruncatedContent` |
| Secret present | Redacted and `SecretRedacted` in plan flags |
| Invalid JSON first attempt | Retry succeeds; iterations=2 |
| Invalid JSON repeated | Error `ModelOutputInvalid` |

## Non-Goals (Phase 2)
- No automatic apply; user must review.
- No semantic ranking or embedding indexing.
- No advanced refactor (rename symbol across project); out-of-scope.
- No speculative plan merging; refinement only by producing a new plan.

## Migration & Backward Compatibility
- Existing plans unaffected (no required new fields).
- New `generation` object is optional.
- JSON schemas distinct from existing `plan.create.*` to avoid confusion.

## Open Questions
- Should we store raw model response for audit? (Optionally behind config; excluded initially for privacy.)
- Introduce cost estimation? (Defer until provider pricing abstraction in place.)

END OF SPEC

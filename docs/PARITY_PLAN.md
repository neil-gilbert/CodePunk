# CodePunk CLI Parity Plan

Version: 1.3.0 (Released)
Last Updated: 2025-09-14

This plan has been refocused on the core "coder CLI" workflow: create agents, run one‑shot prompts or continue sessions, generate and apply structured code change plans. Broader platform / automation features (HTTP server, semantic index, CI helpers, GitHub automation, advanced refactors) are deferred. The intent is to ship a lean but end‑to‑end useful developer loop rapidly to gather real user feedback.

---
## 1. Focused MVP Feature Set (Coder Loop)
Included (MVP scope):
- run (one-shot prompt; continue existing session)
- agent (create/list/show/delete simple agent definitions)
- auth (login/list/logout API provider keys)
- sessions (list/show/load persisted chat sessions)
- plan (generate structured change plan)
- apply (apply a previously generated plan with diff + confirmation)
- models (optional: enumerate provider/model pairs if keys present)

Global flags (initial subset): --model, --agent, --session / --continue, --help, --version (others like --prompt, networking flags deferred).

Deferred (post-MVP): serve, github, upgrade, semantic index, generate-tests, refactor, prompts list/show (beyond internal layering), advanced security scan, config layering, OTel beyond minimal, plugin system, TUI, automation workflows.

---
## 2. Current State Snapshot (v1.2.3)
Implemented:
- Interactive chat loop (REPL) launching when no args provided.
- Auth key store + `auth` CLI (login/list/logout) with file persistence.
- Agent file store + `agent create|list|show|delete`.
- Run command with one‑shot prompt, `--session`, `--continue`, `--agent`, `--model`, and JSON output (`run.execute.v1`).
- Session file store + `sessions list|show|load` with JSON (`sessions.list.v1`, `sessions.show.v1`).
- Plan subsystem: create/list/show/add/diff/apply with persistent plan records, unified diffs, drift detection, dry‑run + force, deletion staging (`--delete`), automatic backups, JSON schemas (`plan.create/add/list/show/diff/apply`).
- Plan create-from-session (Phase 1 COMPLETE): heuristic summarizer wired to `--from-session` producing goal, candidateFiles, rationale, message counts, truncated flag; persisted `PlanSummary` (with source, counts, truncation) and approximate token usage (sampleChars/approxTokens) now stored and surfaced via JSON `tokenUsageApprox`.
- Unified JSON output helper + centralized schema constants (including models.list.v1).
- Deletion support in plan add/apply (actions: deleted, dry-run-delete, skip-missing, delete-error).
- Approximate token usage (char/4 heuristic) surfaced in run JSON output.
- Models command implemented (plain table + JSON schema `models.list.v1`) listing provider/model pairs with key presence flag.
- Test stabilization: enhanced JSON extraction in console tests (ANSI stripping + last-object scan) to make schema parsing resilient to surrounding output.

Remaining (MVP polish / minor):
- (Decision) Token usage remains limited to run + from-session plan summary heuristic; further extension deferred until AI plan generation Phase 2.

Deferred (post-MVP – unchanged): serve mode, semantic index/search, generate-tests, refactor, GitHub automation, upgrade, extended telemetry, advanced security scan, front-matter parser, plugin system, TUI.

---
## 3. Gap Matrix & Priority (Updated)
Completed (MVP Core): 1–6 from prior list (auth, sessions CLI, run JSON, plan persistence + diff/apply, unified JSON output, deletion support).

Remaining Near-Term:
7. Phase 1: `plan create --from-session` implementation (COMPLETED v1.2.3) — includes persisted PlanSummary + tokenUsageApprox in JSON.
8. (Next) Evaluate Phase 2: AI-augmented multi-file plan generation (deferred, not in MVP scope).

Stretch / Deferred: Front-matter prompt parser, extended telemetry toggle, others as previously deferred.

---
## 4. Storage Layout (Planned – Coder Scope)
Global (user home):
- ~/.config/codepunk/auth.json { provider: apiKey }
- ~/.config/codepunk/agents/*.json (agent definitions)
- ~/.config/codepunk/sessions/index.json + sessions/{sessionId}.json

Repo-local (optional overrides):
- .codepunk/config.json
- .codepunk/agents/*.json
- .codepunk/prompts/*.md (extra front-matter prompts)
- .codepunk/index/ (semantic vectors)
- .codepunk/plans/plan-{id}.json
- .codepunk/diff/ (temp diffs if needed)

Cache / Temp:
- .codepunk/tmp/

---
## 5. Core Interfaces (Active Set)
- IAuthStore: SetKey, RemoveKey, ListProviders, LoadAsync/SaveAsync.
- IAgentStore: (Implemented) Create, Get, List, Delete (future: Resolve composite prompt).
- ISessionStore: Create, Append, Get, List.
- IPlanService: GeneratePlanAsync(input) → Plan.
- IDiffService: CreateDiff(original, updated) → Diff (hash for conflict detection).
- IApplyService: ApplyAsync(planId, options) → ApplyResult.
- ITokenUsageRecorder (lightweight wrapper or integrated into LLM provider).

Deferred Interfaces: IIndexService, IModelCatalog (beyond minimal static listing), extended prompt layering front‑matter (optional stretch), advanced telemetry abstractions.

---
## 6. Data Contracts (Implemented Forms)
Plan Record (internal storage – minimal file list model):
```
{
  "definition": { "id": "20250913-abc123", "goal": "Refactor X", "createdUtc": "2025-09-13T12:34:56Z" },
  "files": [
    {
      "path": "src/Foo.cs",
      "beforeContent": "...",          // may be null if new file or delete staged before reading
      "afterContent": "...",           // null for deletion or snapshot-only stage
      "hashBefore": "HEX?",            // SHA256 of beforeContent (if present)
      "hashAfter": "HEX?",             // SHA256 of afterContent (if present)
      "diff": "--- unified diff ---",  // or text marker for deletions
      "rationale": "why the change",    // optional
      "isDelete": true|false
    }
  ]
}
```
Plan JSON output schemas (CLI) expose filtered projections per command; rely on `schema` field (see Section 8).

Token Usage (approximation – run command + plan from-session heuristic):
```
// run.execute.v1 (unchanged)
{
  "promptTokensApprox": n,
  "completionTokensApprox": n,
  "totalTokensApprox": n
}

// plan.create.fromSession.v1 (added in 1.2.3)
{
  "tokenUsageApprox": { "sampleChars": n, "approxTokens": n }
}
```

---
## 7. Prompt Merging Rules (Current State)
Order: Base → Provider → Overrides (sorted by priority asc). Replace resets chain; Append concatenates with blank line.
Maximum size per composite (configurable, default 64 KB). Cache by (provider,type,hash).

---
## 8. Plan → Diff → Apply Workflow (Implemented v1)
1. `plan create --goal` creates persistent plan (empty file list initially).
2. `plan add` stages file changes:
  - Snapshot only (beforeContent) if no `--after-file`.
  - Modification (before + after + diff + hashes) if `--after-file` provided.
  - Deletion via `--delete` (records beforeContent hash + deletion marker diff).
3. `plan diff` shows stored unified diffs (including deletion marker strings).
4. `plan apply`:
  - Optional `--dry-run` (reports actions without writing).
  - Drift detection: if current file hash != stored `hashBefore` and not `--force`, skip with action `skipped-drift`.
  - Backups: original file copies stored under plan-specific backup directory (including deleted files before removal).
  - Actions emitted: applied, dry-run, deleted, dry-run-delete, skip-missing, skipped-drift, skipped-error, delete-error.
5. JSON output (`plan.apply.v1`) includes summary { applied, skipped, drift, backedUp } and per-change details (path, action, hadDrift, backupPath).
6. Re-apply after drift: user can re-stage file or use `--force` to override (still flagged with `hadDrift` true).

---
## 9. (Deferred) Semantic Index
Removed from MVP; revisit after initial user feedback.

---
## 10. CLI Command Specifications (Current Implementation)
run:
- Args: message (optional). If absent, start interactive REPL.
- Flags: --model, --agent, --session|--continue, --prompt (override system prompt), --dry-run (for agent actions producing plan?).

auth:
- login: prompts for provider + api key; writes to auth.json (permissions 0600 POSIX).
- list/ls: enumerates stored providers.
- logout: removes provider key.

agent:
- create: interactive or flags (--name, --provider, --prompt-file, --tools).
- list: show agent names.
- show <name>: print merged prompt.
- delete <name>.

models:
- Lists provider/model pairs discovered from configured keys or static registry.

plan:
- Input: freeform requirement text or path to spec file.
- Output: plan id.

apply:
- Input: plan id.
- Flags: --dry-run, --yes (auto-confirm), --filter <glob>.

prompts:
- list: provider, type, source, priority, mode.
- show: provider + type final composite.

Deferred specs removed (serve, build-index/search, upgrade) – will reintroduce in a separate roadmap once MVP validated.

---
## 11. Telemetry (Minimal for MVP)
Initial: Approx token counts + basic activity spans already present for run/agent operations. Extended tracing & metrics deferred. Environment toggle may be added post-MVP.

---
## 12. Security & Safety Baseline (Unchanged Core)
- Deny path escapes (../ or symlink outside root).
- Skip binary or >1MB by default (configurable).
- Secret pattern detection (basic regex) redacts before persistence.
- Malicious code heuristics: refuse with short message.

---
## 13. Phased Delivery (Progress Update)
Phase A: Auth + Sessions + Run JSON – COMPLETE.
Phase B: Plan persistence + Diff + Apply + Deletion – COMPLETE.
Phase C: Unified JSON schemas – COMPLETE (token usage partially: run only).
Phase D: Docs & integration scenario test – INITIAL (README + placeholder integration test present; full CLI E2E test pending optional).
Phase E: Phase 1 Session-Derived Plan Generation – COMPLETE (v1.2.3).
Phase F (AI Plan Generation Phase 2) – COMPLETE (v1.3.0):
  - Implemented command: `plan generate --ai` producing schema `plan.generate.ai.v1`.
  - Added provider/model override flags with default provider fallback.
  - Implemented `PlanGeneration` metadata: provider, model, prompt/completion/total tokens, iterations, safetyFlags, createdUtc.
  - Extended `PlanFileChange` with `Generated` flag and per-file `Diagnostics` (UnsafePath, SecretRedacted, TruncatedContent, TruncatedAggregate).
  - Safety & limits enforced via configurable `PlanAI` options (MaxFiles, MaxPathLength, MaxPerFileBytes, MaxTotalBytes, RetryInvalidOutput, SecretPatterns).
  - UTF-8 safe truncation for per-file rationale and aggregate cap with diagnostics.
  - Secret redaction for configured patterns replaced with `<REDACTED>` and flagged.
  - Retry loop for invalid JSON model output (error codes: ModelUnavailable, ModelOutputInvalid, TooManyFiles).
  - Token usage captured from provider response when available.
  - Documentation: README updated (usage, schema, safety config), help command enhanced, parity plan updated.
  - Tests added: success parsing, invalid JSON retry, safety (TooManyFiles, UnsafePath, SecretRedaction), truncation (per-file + aggregate), config override (MaxFiles=1), token usage presence.
  - Non-goals confirmed: no automatic apply, no semantic ranking/index, no refinement loop yet.

---
## 14. Risk & Mitigation (Still Relevant)
- Scope creep → Enforce guardrails doc.
- Diff conflicts → Hash verification + conflict status.
- Token inaccuracy → Fallback approximate token count (char/4 heuristic) if provider silent.
- Secrets leak → Redaction filter on message persistence + diff output.
- Performance on large repos → Configurable indexing file glob + size cap.

---
## 15. Definition of Done (Updated – Coder Loop)
MVP DoD now considered met when:
1. Auth, agents, sessions, run, plan (create/add/diff/apply with deletion) all operational with JSON schemas.
2. Approx token usage present in run JSON output.
3. README documents coder workflow & schemas.
4. Plan deletion actions covered by tests.
5. Unified JSON schema tests pass (current console tests green).
6. Optional: models command may be added without blocking MVP.

---
## 16. Backlog (Deferred After MVP)
Serve mode, semantic index/search, generate-tests, refactor rename, upgrade helper, GitHub automation, advanced security scan, config layering, provider fallback, extended telemetry/metrics, plugin system, TUI, tool registry, agent export/import, prompt hot-reload, embedding abstraction, advanced refactor actions, policy mutators.

---
## 17. Resumption Checklist (If Picking Up Later)
1. Check implemented interfaces vs Section 5.
2. Run tests; add tests for any newly added interface contract.
3. Verify prompt front-matter parser presence.
4. Confirm OTel spans emitted for run/plan/apply.
5. Review open TODO comments for drift.

---
## 18. Change Log
2025-09-14 (v1.3.0): Completed Phase 2 AI plan generation (`plan generate --ai`), added safety enforcement (path/file count/size, secret redaction, truncation), token usage capture, configurable `PlanAI` options, diagnostics, tests, help/README updates.
2025-09-14 (v1.2.3): Completed Phase 1 session-driven plan creation: persisted PlanSummary (goal, candidateFiles, rationale, message counts, truncated flag, token usage heuristic). Added JSON `tokenUsageApprox` for `plan create --from-session`, backward compatibility tests, and updated specs.
2025-09-13 (v1.2.1): Added models command implementation (`models.list.v1`), improved test robustness (ANSI escape stripping + JSON last-object detection), fallback JSON output path when no ANSI console, documentation updates.
2025-09-13 (v1.2.0): Implemented run JSON output, sessions CLI JSON, plan persistence with diff/apply + deletion & backups, unified JSON schema constants, approximate token usage (run), README & docs updates.
2025-09-13 (v1.1.0): Narrowed scope to coder CLI MVP; reclassified server/index/refactor/test/automation features as deferred; updated DoD & phased delivery.

---
## 19. Glossary
- Agent: Named configuration referencing provider + system prompt + tool set.
- Plan: Structured JSON description of intended multi-file changes.
- Diff: Unified text representation of file modifications based on original hash.
- Dry-run: Execution mode that computes changes without writing.
- Semantic Index: Embedding-based search structure.
- Front-matter: YAML header controlling prompt merge behavior.

---
END OF DOCUMENT

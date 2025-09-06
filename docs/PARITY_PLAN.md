# CodePunk CLI Parity Plan

Version: 1.0.0
Last Updated: 2025-09-06

This document tracks progress toward feature parity with the opencode.ai CLI.
It is implementation-focused and can be resumed at any time by a contributor or AI assistant.

---
## 1. Reference Feature Set (Target)
Commands to conceptually mirror:
- run (one-shot non-interactive prompt execution; session continuation)
- agent (create/manage agent definitions: system prompt + tools)
- auth (login/list/logout providers & store keys)
- models (list available provider/model pairs)
- serve (headless HTTP server exposing API)
- github (install/run automation workflow) – optional for MVP
- upgrade (self-update helper / guidance)

Global flags parity: --model, --agent, --prompt, --session / --continue, --port, --hostname, --help, --version.

---
## 2. Current State Snapshot
Implemented:
- Interactive chat session (manual) with file read/write tools.
- Prompt layering: Base + Provider prompts + external overrides (env path).
- Anthropic optimized provider prompt.
- Basic tests for PromptProvider.

Missing / Partial:
- Structured CLI subcommand framework.
- Agent definitions persistence (none yet).
- Auth key storage and provider management.
- Model listing.
- Plan → Diff → Apply workflow.
- Diff preview & dry-run.
- Semantic search / indexing.
- Test generation / refactor commands.
- Session persistence (resume by ID).
- OTel tracing implementation (planned).
- Front-matter prompt merging (Append/Replace) not parsed yet.
- HTTP server mode (serve).
- Token usage accounting wrapper.
- Upgrade / github integration.

---
## 3. Gap Matrix & Priority
P0 (Baseline Parity Core):
1. CLI command router (System.CommandLine recommended).
2. run command (--model, --agent, --session / --continue).
3. auth (login/list/logout) with secure local JSON store.
4. agent (create/list/show/remove) storing definitions.
5. models (aggregate from providers with keys).
6. Session persistence service & storage layout.
7. Plan + Diff + Apply workflow (foundation).
8. Dry-run apply mode.
9. Token usage capture (basic counters).
10. Front-matter parser for prompt overrides.
11. OpenTelemetry tracing skeleton.

P1 (Enhanced Parity):
12. serve HTTP mode (chat, plan, apply, search endpoints).
13. Semantic index (build-index, search).
14. generate-tests command.
15. refactor (rename symbol) command.
16. prompts list/show.
17. upgrade helper command.
18. Basic security scan (secret patterns + path enforcement).

P2 (Extended Quality):
19. github automation scaffolder.
20. Tool registry list/enable/disable.
21. Agent export/import.
22. Config file layering (global + repo local).
23. Provider fallback strategy.
24. Extended OTel metrics & spans.
25. Embedding provider abstraction.

P3 (Deferred / Nice-to-Have):
26. Plugin system (custom tools).
27. Prompt directory hot-reload.
28. Advanced refactor actions (extract method, API migration).
29. Policy mutators (org rules injection).
30. Rich TUI.

---
## 4. Storage Layout (Planned)
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
## 5. Core Interfaces To Implement / Extend
- IAuthStore: LoadAsync, SaveAsync, SetKey, RemoveKey, ListProviders.
- IAgentStore: Create, Get, List, Delete, Resolve(agentName) → composite prompt.
- ISessionStore: CreateSession, AppendMessage, GetSession, ListSessions.
- IPlanService: GeneratePlanAsync(input) → Plan.
- IDiffService: CreateDiff(original, updated) → Diff (with hash).
- IApplyService: ApplyAsync(planId, options) → ApplyResult.
- IIndexService: BuildAsync(root), QueryAsync(text, topK).
- IModelCatalog: ListModels(providerFilter?).
- ITokenUsageRecorder (or embed inside ILLMService wrapper).

Existing to extend: IPromptProvider (front-matter parsing).

---
## 6. Data Contracts (Versioned)
Plan v1:
```
{
  "version": 1,
  "summary": "string",
  "steps": [
    {
      "id": "S1",
      "intent": "string",
      "files": [ { "path": "src/..", "action": "modify|create|delete" } ],
      "notes": "optional string"
    }
  ]
}
```
Diff:
```
{
  "path": "string",
  "originalHash": "sha256",
  "newHash": "sha256",
  "unified": "--- diff text ---",
  "action": "modify|create|delete"
}
```
PromptDefinition front-matter YAML:
```
---
mode: Append|Replace
priority: 120
description: Optional summary
---
<markdown content>
```
TokenUsage:
```
{ "model": "provider/model", "inputTokens": n, "outputTokens": n }
```

---
## 7. Prompt Merging Rules
Order: Base → Provider → Overrides (sorted by priority asc). Replace resets chain; Append concatenates with blank line.
Maximum size per composite (configurable, default 64 KB). Cache by (provider,type,hash).

---
## 8. Plan → Diff → Apply Workflow (MVP)
1. plan (input prompt) → Plan JSON stored (.codepunk/plans/plan-{id}.json).
2. apply [planId] → generate proposed file states → diffs.
3. Show unified diffs; if --dry-run stop after display.
4. Require confirmation (all or interactive) then write atomically.
5. Post-apply: print summary {files changed, created, deleted, token usage}.
6. Conflicts: if current file hash != originalHash, mark conflict; skip apply; user must re-plan.

---
## 9. Semantic Index (MVP)
- build-index: traverse repo (respect ignore patterns), chunk text (e.g., ~800 tokens logical boundaries).
- Store embeddings in JSON or lightweight binary (embedding store file).
- search: query → embed → cosine similarity topK → return path + line snippet contexts.
- Rebuild required after significant code changes (no auto watch).

---
## 10. CLI Command Specifications (Initial)
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

build-index / search (P1): straightforward.

serve (P1): minimal endpoints: /api/chat, /api/plan, /api/apply, /api/search.

upgrade (P1): prints instructions for `dotnet tool update --global CodePunk`.

---
## 11. OpenTelemetry Tracing (Incremental)
P0 Spans: run, plan, apply root; LLM.Call; Apply.File; PromptResolve.
P1 Spans: Index.Build, Index.Query, Refactor.Rename, Test.Generate.
Metrics: llm.tokens.input/output, apply.files.changed, plan.duration.ms (hist), llm.latency.ms (hist), index.size.chunks (gauge).
Disable via CODEPUNK_TRACING=0.

---
## 12. Security & Safety Baseline
- Deny path escapes (../ or symlink outside root).
- Skip binary or >1MB by default (configurable).
- Secret pattern detection (basic regex) redacts before persistence.
- Malicious code heuristics: refuse with short message.

---
## 13. Implementation Phases (Sprint Breakdown)
Sprint 1 (P0 Core): CLI framework, auth store, agent store, session persistence, run command, basic model listing, OTel skeleton.
Sprint 2: Plan service, diff + apply, dry-run, token usage hook, prompt front-matter parser, prompts list/show.
Sprint 3: Semantic index (build/search), test generation, refactor rename, serve mode, security scan.
Sprint 4: upgrade command, config layering, extended metrics, error classification, agent export/import.
Sprint 5: github automation stub, tool registry mgmt, provider fallback, prompt hot-reload (optional), embedding abstraction.

---
## 14. Risk & Mitigation
- Scope creep → Enforce guardrails doc.
- Diff conflicts → Hash verification + conflict status.
- Token inaccuracy → Fallback approximate token count (char/4 heuristic) if provider silent.
- Secrets leak → Redaction filter on message persistence + diff output.
- Performance on large repos → Configurable indexing file glob + size cap.

---
## 15. Definition of Done (Parity Baseline)
All P0 items implemented + successful manual test sequence:
1. auth login provider
2. agent create sample-agent
3. run --agent sample-agent "Add a helper class"
4. plan "Refactor X into Y" → plan id
5. apply plan id (diff preview, confirmation, apply) 
6. prompts show provider Coder displays merged prompt
7. models lists available models
8. Token usage summary appears
9. OTel spans visible (run, plan, apply)

---
## 16. Backlog (Deferred After Parity)
Plugin system, advanced refactors, policy mutators, multi-provider fallback, TUI enhancements, full GitHub automation, prompt diff/export tools, continuous indexing watch.

---
## 17. Resumption Checklist (If Picking Up Later)
1. Check implemented interfaces vs Section 5.
2. Run tests; add tests for any newly added interface contract.
3. Verify prompt front-matter parser presence.
4. Confirm OTel spans emitted for run/plan/apply.
5. Review open TODO comments for drift.

---
## 18. Change Log
(Empty – populate on first update.)

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

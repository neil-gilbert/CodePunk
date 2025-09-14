# SPEC: plan create --from-session

Purpose: provide a deterministic, minimal heuristic to create an initial plan from recent session context. This is Phase 1; model-based summarization is a later addition.

Inputs
- `--from-session` (bool) — enable session-based plan creation
- `--session <id>` (optional) — explicit session id, otherwise choose most recently updated session
- `--messages <N>` (optional, default 20) — number of recent user+assistant messages to consider
- `--include-tools` (optional) — include tool messages in summarization

Output
- On success (JSON): `plan.create.fromSession.v1` object containing:
  - `schema`: "plan.create.fromSession.v1"
  - `planId`: created plan id
  - `goal`: inferred short goal string
  - `candidateFiles`: array of file path hints (may be empty)
  - `messageSampleCount`: number of messages used
  - `truncated`: boolean
  - `rationale`: optional short explanation (currently embedded only in persisted PlanSummary, may be added to JSON later)
  - `tokenUsageApprox`: object `{ sampleChars, approxTokens }` (heuristic: (goal.Length + rationale.Length + Σ(filePath.Length+1)) / 4)

Failure Modes
- No session found: emit error object in JSON with code `SessionNotFound` and human error when not `--json`.
- Insufficient context (<2 user messages): emit `InsufficientSessionContext` and fall back to manual plan creation.

Behavior Notes
- CandidateFiles are only hints (not staged or modified); they are expected to help the developer refine the plan.
- The summarizer is deterministic and uses regex/file hint extraction; no network calls in Phase 1.
- The persisted plan record now includes a `summary` object (PlanSummary) storing: source ("session"), goal, candidateFiles, rationale, usedMessages, totalMessages, truncated, tokenUsage (sampleChars, approxTokens). Legacy plan records without `summary` remain valid.

Schema Versioning
- Add new schema `plan.create.fromSession.v1`. Do not change existing `plan.create.v1`.

Acceptance Criteria
- SPEC file added and referenced in parity plan.
- Tests cover empty/no-session and a basic multi-message session producing goal + candidateFiles.

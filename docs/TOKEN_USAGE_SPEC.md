# Token Usage Spec (Phase 1 stub)

This document outlines the Phase-1 design for lightweight token usage tracking across streaming LLM interactions.

Scope
- Minimal provider-agnostic interface for recording prompt/completion tokens and estimated cost.
- Streaming accumulation rules and fallback heuristic (chars/4) for providers that don't expose token counts per chunk.

Proposal
- Interface: `IUsageMetricsProvider` (returns InputTokens, OutputTokens, EstimatedCost)
- Streaming: accumulate per chunk provided by LLM provider; when chunk lacks token metadata, estimate tokens = ceil(chars / 4.0).
- Session-level: record PromptTokens, CompletionTokens, Cost in session metadata and persist at stream completion (batched updates).

JSON: optionally add `usage` object to `run.execute.v1` and future `plan.create.fromSession.v1` responses in a backward-compatible manner (new property, no breaking change).

Acceptance: documented design, unit tests for accumulation logic, and small integration test with mocked provider streaming token metadata.

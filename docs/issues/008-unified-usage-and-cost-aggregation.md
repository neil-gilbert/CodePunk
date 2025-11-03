Title: Unify usage/cost aggregation across providers and streaming modes

Labels: area:telemetry, type:enhancement

Context
- Usage is populated differently per provider and not consistently updated during/after streaming in one place.
- We need a single aggregator translating provider usage into `LLMUsage` and applying session totals.

References
- src/CodePunk.Core/Providers/OpenAIProvider.cs:308 (usage mapping)
- src/CodePunk.Core/Providers/Anthropic/AnthropicProvider.cs:753 (usage mapping)
- src/CodePunk.Core/Chat/InteractiveChatSession.cs:662 (usage rollup at stream end)

Proposed Approach
- Introduce a small usage aggregator utility that:
  - Captures prompt/completion tokens and estimated cost by model.
  - Provides a finalization hook for streaming to update session totals.
- Replace ad-hoc usage mapping in providers with calls to the aggregator.

Acceptance Criteria
- Session usage (tokens, cost) updates consistently for both streaming and non-streaming calls.
- Providers feed usage into the same path; cost model remains as-is but centralized.


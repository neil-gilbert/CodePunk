Title: Implement token-aware history truncation before request build

Labels: area:llm, type:enhancement

Context
- We currently pass full message history and rely on provider-side limits. A consistent token budget policy improves reliability and cost control.
- Microsoft.Extensions.AI patterns encourage counting tokens (when available) and trimming proactively.

References
- src/CodePunk.Core/Services/LLMService.cs:133 (ConvertMessagesToRequest)
- src/CodePunk.Core/Services/LLMTokenService.cs:21 (Anthropic token counting)

Proposed Approach
- Add a truncation step before building `LLMRequest` that:
  - Uses provider-native token counting when available to target a budget (context window minus generation budget and overhead).
  - Falls back to heuristic byte/char limits when counting unavailable.
- Prefer keeping system prompt + latest few turns + any tool messages that are still relevant.

Acceptance Criteria
- For long histories, the request is trimmed to fit a configurable token budget without provider errors.
- Behavior documented; option to disable truncation for debugging.


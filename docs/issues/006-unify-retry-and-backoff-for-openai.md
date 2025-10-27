Title: Unify OpenAI retry/backoff with Anthropic policy (Retry-After + jitter)

Labels: area:llm, provider:openai, type:enhancement

Context
- Anthropic provider has robust retry logic including Retry-After handling and exponential backoff with jitter.
- OpenAI provider currently uses a single `EnsureSuccessStatusCode` without retry looping.

References
- src/CodePunk.Core/Providers/Anthropic/AnthropicProvider.cs:95 (retry policy creation and use)
- src/CodePunk.Core/Providers/OpenAIProvider.cs:153 (non-streaming send)
- src/CodePunk.Core/Providers/OpenAIProvider.cs:166 (streaming send)

Proposed Approach
- Extract a shared helper/policy for transient failure handling (429/503, Retry-After) similar to the Anthropic implementation.
- Apply to both OpenAI non-streaming and streaming endpoints.
- Keep a conservative max retry count and respect server-provided Retry-After.

Acceptance Criteria
- OpenAI transient 429/503 failures are retried according to policy, respecting Retry-After where provided.
- No regressions to successful first-attempt behavior.
- Logging includes attempt counts and delays at debug/info level.


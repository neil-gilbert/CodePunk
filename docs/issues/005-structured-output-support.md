Title: Add structured-output support (typed JSON responses) across providers

Labels: area:llm, type:enhancement

Context
- Structured outputs reduce brittle parsing and are a recommended pattern in Microsoft.Extensions.AI.
- OpenAI supports `response_format` with JSON/JSON schema; Anthropic can be guided to output strict JSON via prompt nudges.

References
- src/CodePunk.Core/Abstractions/ILLMProvider.cs:47 (LLMRequest)
- src/CodePunk.Core/Providers/OpenAIProvider.cs:268 (request mapping)
- src/CodePunk.Core/Providers/Anthropic/AnthropicProvider.cs (request/prompt construction)

Proposed Approach
- Extend `LLMRequest` with a `ResponseFormat` contract and, when provided, set OpenAI `response_format` accordingly.
- For Anthropic, add a mode that injects a small system nudge to produce strict JSON for the requested schema and parse the response to a typed model.
- Add helper `TryParseJson<T>` to centralize decoding and error handling.

Acceptance Criteria
- Callers can request typed responses specifying a target model/schema.
- OpenAI path enforces JSON structure via `response_format`.
- Anthropic path gracefully nudges and parses; errors are surfaced with actionable messages.


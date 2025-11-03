Title: Extend LLMRequest (stop, tool choice, penalties, response format) and map to OpenAI

Labels: area:llm, provider:openai, type:enhancement

Context
- We’re missing several request knobs that Microsoft.Extensions.AI commonly supports: `stop`, `seed`, `frequency/presence_penalty`, `tool_choice`, and `response_format` (JSON schema mode).
- These help enforce first-turn tool selection, structured outputs, and fine-tune behavior.

References
- src/CodePunk.Core/Abstractions/ILLMProvider.cs:47 (LLMRequest)
- src/CodePunk.Core/Providers/OpenAIProvider.cs:268 (ConvertToOpenAIRequest)
- src/CodePunk.Core/Services/LLMService.cs:150 (first-turn restricted tools)

Proposed Approach
- Add to `LLMRequest`:
  - `string[]? Stop`, `int? Seed`, `double? FrequencyPenalty`, `double? PresencePenalty`,
  - `string? ToolChoice` (values: `auto`, `required`, `none`),
  - `ResponseFormat?` (type that can represent `json`, or custom json_schema with a provided schema object/name/version).
- Update OpenAI mapper to set these fields when present: `stop`, `seed`, `frequency_penalty`, `presence_penalty`, `tool_choice`, `response_format`.
- In `LLMService`, when restricting to first-turn tools, set `ToolChoice = "required"` to force a tool call.

Acceptance Criteria
- New fields compile across the project; OpenAI requests include mapped properties when set.
- First-turn mode selection enforces a tool call when we restrict to `mode_plan`/`mode_bug`.
- Backward compatibility: Default behavior remains unchanged when new fields are null.


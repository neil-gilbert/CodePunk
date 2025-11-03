Title: Add Activity/OTel spans for LLM calls and streaming

Labels: area:telemetry, type:enhancement

Context
- Microsoft.Extensions.AI encourages consistent observability. We can add ActivitySource spans around send/stream operations for tracing and metrics.

References
- src/CodePunk.Core/Services/LLMService.cs:176 (send path)
- src/CodePunk.Core/Services/LLMService.cs:184 (stream path)
- src/CodePunk.Core/Chat/InteractiveChatSession.cs:296 (streaming conversation)

Proposed Approach
- Add an `ActivitySource` for CodePunk AI operations.
- Create spans for request preparation, provider call, stream consumption, and finalize.
- Tag provider, model, token usage (when known), and error status.

Acceptance Criteria
- Spans appear when a tracer is configured, including key tags.
- No functional regressions; overhead is minimal.


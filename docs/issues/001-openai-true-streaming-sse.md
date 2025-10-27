Title: Fix OpenAI provider to use true streaming (SSE) with ResponseHeadersRead

Labels: area:llm, provider:openai, type:enhancement

Context
- Our OpenAI provider sets `stream=true` but uses `PostAsJsonAsync` which defaults to buffering (`ResponseContentRead`). This can delay streaming and increase memory usage.
- We should request `ResponseHeadersRead`, set appropriate headers, and read the response stream incrementally.

References
- src/CodePunk.Core/Providers/OpenAIProvider.cs:162
- src/CodePunk.Core/Providers/OpenAIProvider.cs:166
- src/CodePunk.Core/Providers/OpenAIProvider.cs:169
- src/CodePunk.Core/Providers/OpenAIProvider.cs:172

Proposed Approach
- Use `HttpRequestMessage` + `HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)` for the streaming call.
- Set `Accept: text/event-stream` when `stream=true`.
- Keep current SSE line parsing, but ensure we do not buffer the entire response.
- Ensure `CancellationToken` is flowed to both `SendAsync` and the stream reader.

Acceptance Criteria
- Streaming responses begin within ~1 RTT instead of after the full completion.
- No buffering of the full response content (verified by code path and manual test on a long prompt).
- Existing component tests that depend on streaming continue to pass; add/extend one test that validates incremental chunks are surfaced.

Risks / Notes
- Keep current JSON-chunk logic for compatibility; this change is transport-level.


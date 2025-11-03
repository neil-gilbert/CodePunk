Title: Propagate CancellationToken through OpenAI SSE reader and HTTP calls

Labels: area:llm, provider:openai, type:bug

Context
- The OpenAI streaming loop does not pass the `CancellationToken` to `ReadLineAsync`, which can make cancellation sluggish when the user aborts.
- Ensure cancellation flows to both the HTTP request and the line reader.

References
- src/CodePunk.Core/Providers/OpenAIProvider.cs:172
- src/CodePunk.Core/Providers/OpenAIProvider.cs:174

Proposed Approach
- Use `await reader.ReadLineAsync(cancellationToken)` to allow prompt cancellation.
- Confirm `SendAsync(..., cancellationToken)` is used for the HTTP call.

Acceptance Criteria
- Cancelling a streaming request (e.g., via CTS) reliably stops the read loop within 200ms.
- No regressions to streaming behavior.


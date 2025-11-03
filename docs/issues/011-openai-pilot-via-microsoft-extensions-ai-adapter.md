Title: Pilot OpenAI via Microsoft.Extensions.AI adapter behind ILLMProvider

Labels: area:llm, provider:openai, type:spike

Context
- To learn from the library without a big-bang migration, implement a thin adapter that satisfies `ILLMProvider` over `IChatClient` for OpenAI.
- This lets us compare streaming/tool-call behavior and test coverage while keeping Anthropic as-is (preserving special events).

References
- src/CodePunk.Core/Abstractions/ILLMProvider.cs
- src/CodePunk.Core/Services/LLMProviderFactory.cs
- src/CodePunk.Infrastructure/Configuration/ServiceCollectionExtensions.cs

Proposed Approach
- Add Microsoft.Extensions.AI provider packages for OpenAI.
- Register a named `IChatClient` and create an adapter class `ExtensionsAiOpenAiProvider : ILLMProvider` mapping between our request/response types and the chat client’s.
- Swap DI to prefer the adapter when the package is present/configured; keep current OpenAI provider available behind a flag to ease rollback.

Acceptance Criteria
- All existing tests pass with the adapter enabled for OpenAI.
- Streaming/tool-calls behave equivalently for our core paths.
- Document observed differences and any follow-ups.

Notes
- Network access and package restore may require CI/environment changes.


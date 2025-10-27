Title: Add ImagePart support and multimodal scaffolding

Labels: area:llm, type:enhancement

Context
- Microsoft.Extensions.AI treats image content as a first-class part of chat messages. Adding an `ImagePart` now future-proofs us for vision/multimodal providers.

References
- src/CodePunk.Core/Models/Message.cs (message model)
- src/CodePunk.Core/Providers/OpenAIProvider.cs:206 (message conversion)
- src/CodePunk.Core/Providers/Anthropic/AnthropicProvider.cs (message conversion)

Proposed Approach
- Introduce `ImagePart` (with URI/path or byte[] + mime type) in our message model.
- Update provider mappers to include image content for vendors that support it; ignore for others.
- No UI changes required initially; just wire the data model and conversion.

Acceptance Criteria
- Code compiles with optional `ImagePart`.
- Providers compile; image parts are mapped where supported or skipped otherwise.
- No behavior change for pure text flows.


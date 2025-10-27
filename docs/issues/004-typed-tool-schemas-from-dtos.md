Title: Generate tool JSON Schema from typed DTOs using DataAnnotations

Labels: area:tools, type:enhancement

Context
- We manually provide `LLMTool.Parameters` as `JsonElement`, which can drift from actual runtime expectations. The Microsoft.Extensions.AI approach derives schemas from method signatures/attributes.

References
- src/CodePunk.Core/Abstractions/ILLMProvider.cs:119 (LLMTool)
- src/CodePunk.Core/Tools/SearchFilesTool.cs
- src/CodePunk.Core/Tools/GlobTool.cs

Proposed Approach
- Introduce DTOs for tool inputs and decorate with DataAnnotations (`[Required]`, `[StringLength]`, `[Range]`, etc.).
- Add a small schema builder that reflects DTOs to JSON Schema and injects into `LLMTool.Parameters` at registration time.
- Convert 1–2 tools (e.g., `search_files`, `glob`) as an initial slice.

Acceptance Criteria
- Selected tools are declared via DTOs, and generated JSON Schema matches DataAnnotations.
- Providers receive consistent, validated schemas; tool calls parse into DTO instances with validation.
- Backward compatibility for other tools; conversions can be incremental.


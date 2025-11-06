# Roslyn Tool Flow (Enumerated)

1. CLI receives invocation (direct or via chat tool-call):
   - Direct: `codepunk roslyn explain-symbol --file src/Foo.cs --line 42 --column 13 --json`
   - Chat: LLM emits tool call `{ name: "roslyn_explain_symbol", arguments: {...} }`.
2. `IToolService` resolves `roslyn_explain_symbol` and executes it with bound args.
3. Tool initializes `IRoslynWorkspaceService` (register MSBuildLocator, load solution/project, cache).
4. Tool delegates to `IRoslynAnalyzerService.ExplainSymbolAsync` to resolve the symbol and gather metadata.
5. Tool composes compact JSON `{ schema: "codepunk.roslyn.symbol.v1", symbol: {...} }` and returns it as `ToolResult.Content`.
6. For refactors: `IRoslynRefactorService` produces `RoslynEditBatch`; `IRoslynPlanBuilder` maps edits to `PlanFileChange` with diffs; tool returns `{ schema: "codepunk.roslyn.plan.v1", planId: ..., filesChanged: N }`.
7. User reviews diffs with `codepunk plan diff --id <planId>`; applies with `codepunk plan apply --id <planId>`.


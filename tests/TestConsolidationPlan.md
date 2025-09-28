# Test Consolidation Plan: From 155+ Unit Tests to ~40 Component Tests

## Current State Analysis
- **95 test files** with **155+ individual tests**
- Many brittle unit tests testing implementation details
- Heavy mocking of internal classes
- Tests break on refactoring even when behavior unchanged

## Target: Component Tests (75% Reduction)

### New Component Test Structure

#### 1. **UserWorkflowTests** (replaces 30+ unit tests)
**Covers:** File operations, approval workflows, user interactions
**Replaces:**
- FileEditServiceTests
- WriteFileToolTests
- ReplaceInFileToolTests
- ApprovalServiceTests
- ToolExecutionHelperTests
- FileValidationTests

**Tests:**
- ✅ User writes file → sees diff → approves → file created
- ✅ User sees large diff → cancels → no changes made
- ✅ User enables session auto-approval → multiple files created
- ✅ User tries invalid path → gets error → no file changes

#### 2. **ChatSessionBehaviorTests** (replaces 20+ unit tests)
**Covers:** Chat flows, streaming, tool loops, session management
**Replaces:**
- InteractiveChatSessionTests
- InteractiveChatSessionToolLoopTests
- InteractiveChatSessionMaxIterationTests
- SessionServiceTests
- MessageServiceTests
- ChatSessionEventTests
- StreamingResponseTests

**Tests:**
- ✅ User sends message → receives streaming response
- ✅ AI makes tool calls → executes in sequence → returns result
- ✅ Tool calls exceed limit → stops with fallback message
- ✅ Session persists → loads previous messages

#### 3. **AIProviderIntegrationTests** (replaces 15+ unit tests)
**Covers:** LLM provider interactions, model switching, auth
**Replaces:**
- AnthropicProviderTests
- OpenAIProviderTests
- LLMProviderFactoryTests
- AuthTests
- ModelTests

**Tests:**
- ✅ User switches provider → uses new provider for requests
- ✅ Provider auth fails → shows clear error message
- ✅ Model unavailable → falls back to default model

#### 4. **CLICommandBehaviorTests** (replaces 25+ unit tests)
**Covers:** Command parsing, execution, output formatting
**Replaces:**
- RunCommandTests
- PlanCommandTests
- AuthCommandTests
- AgentCommandTests
- NewCommandTests
- CommandParsingTests

**Tests:**
- ✅ User runs `codepunk run "create file"` → file created
- ✅ User runs `codepunk plan` → plan generated and saved
- ✅ User runs invalid command → shows helpful error

#### 5. **DataPersistenceTests** (replaces 20+ unit tests)
**Covers:** File storage, session data, configuration
**Replaces:**
- SessionFileStoreTests
- PlanFileStoreTests
- AuthFileStoreTests
- ConfigurationTests

**Tests:**
- ✅ Session created → data persisted → reloads correctly
- ✅ Plan saved → file written → can be loaded later
- ✅ Auth configured → credentials stored securely

#### 6. **DiffAndApprovalBehaviorTests** (replaces 15+ unit tests)
**Covers:** Diff generation, approval UI, user cancellation
**Replaces:**
- DiffServiceTests
- ConsoleApprovalServiceTests
- DiffBuilderTests
- ApprovalResultTests

**Tests:**
- ✅ File changed → diff generated → shows colored output
- ✅ User cancels → operation stops → returns to prompt
- ✅ Large diff → shows truncated preview → user can approve

### What We're Removing (Examples)

#### ❌ Brittle Unit Tests to Delete:
```csharp
// These test implementation details, not behavior
[Fact] public void FileEditService_ValidateFilePath_Returns_Expected_ValidationResult()
[Fact] public void DiffService_ComputeStats_With_EmptyStrings_Returns_ZeroStats()
[Fact] public void ToolExecutionHelper_ExecuteSingleToolCall_Sets_CorrectProperties()
[Fact] public void ConsoleApprovalService_HandleApproval_Logs_ExpectedMessage()
[Fact] public void InteractiveChatSession_ToolIteration_Increments_Correctly()
```

#### ✅ Replaced by Behavior Tests:
```csharp
// These test user-visible behavior and outcomes
[Fact] public async Task User_RequestsFileWrite_AI_ShowsDiff_User_Approves_FileIsWritten()
[Fact] public async Task User_RequestsInvalidFileOperation_ReceivesErrorMessage_NoFileChanges()
[Fact] public async Task ChatSession_ToolCallsExceedLimit_StopsWithFallbackMessage()
[Fact] public async Task User_CancelsOperation_ReturnsToPromptImmediately()
```

## Benefits of Component Tests

### ✅ **Less Brittle**
- Test behavior, not implementation
- Survive refactoring when behavior unchanged
- Focus on user outcomes

### ✅ **Better Coverage**
- Test integration between components
- Catch real bugs that unit tests miss
- Verify complete workflows work

### ✅ **Easier Maintenance**
- 75% fewer tests to maintain
- Clearer test intent and purpose
- Less mocking complexity

### ✅ **Faster Development**
- Don't break on internal refactoring
- Focus on shipping features, not fixing tests
- Tests document actual user workflows

## Implementation Plan

1. **Create component test project** ✅
2. **Write behavior-driven tests for core workflows** 🔄
3. **Run both old and new tests in parallel**
4. **Verify component tests catch the same issues**
5. **Delete brittle unit tests in batches**
6. **Update CI to run only component tests**

## Success Metrics
- **75% reduction in test count:** 155 → ~40 tests
- **Same or better bug detection**
- **Faster test execution** (fewer, but more comprehensive)
- **Reduced test maintenance burden**
- **Better documentation of system behavior**
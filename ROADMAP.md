# CodePunk Roadmap / Remaining Work

Status legend: ✅ done · 🔄 in-progress · ⏳ planned · 🧪 needs tests · 🧹 refactor · ❗ open decision

## 1. CLI & UX
- ⏳ Enhance `models` command: enumerate real models from providers (OpenAI / Anthropic) instead of placeholder
- ⏳ Add `--json` output option for `run`, `auth list`, `agent list`, `models`
- ⏳ Graceful error when no providers / API keys configured (clear guidance to run `codepunk auth login`)
- ⏳ Root-level `sessions` management (list, show, load) mirrored from interactive commands
- ⏳ Add `--provider` / `--model` flags to `run` that override agent defaults (already partially supported internally, needs docs & tests)
- ⏳ Interactive: command autocompletion (tab) & history persistence

## 2. Chat / Session Core
- 🔄 Temporary timing fix uses `Task.Delay(1)` to surface `IsProcessing`; replace with event-based or `IProgress` notification (remove artificial delay)
- ⏳ Session pruning / archive strategy (size limits, rotation)
- ⏳ Export session to markdown / JSON command
- ⏳ Import session from JSON file

## 3. Providers & Models
- ⏳ Azure OpenAI provider implementation
- ⏳ Local model provider(s): Ollama + LM Studio
- ⏳ Dynamic provider discovery via configuration section scanning
- ⏳ Model capability metadata (max tokens, supports tools, streaming) exposed to UX

## 4. Tooling System
- ⏳ Add file search / grep tool (fast code reference)
- ⏳ Add repository indexing + semantic search tool (embeddings or local vector store)
- ⏳ Sandboxed shell execution with configurable allow/block lists
- ⏳ Timeout & cancellation for long-running tools with visible progress

## 5. Prompt Layering / Orchestration
- 🔄 Base + provider prompt layering implemented; missing: user/workspace custom layer injection & merge strategy docs
- ⏳ Front-matter prompt composition system (YAML metadata + body)
- ⏳ Prompt cache invalidation & debug dump command

## 6. Observability & Telemetry
- ⏳ Expand OpenTelemetry: traces for provider requests (span per tool call & model streaming)
- ⏳ Structured logging enrichment (session id, short message id, tool names)
- ⏳ Optional persistent trace export (OTLP / file) toggle

## 7. Security & Secrets
- ⏳ Optional encryption of auth store at rest
- ⏳ Mask provider names & partial keys in logs
- ⏳ Validate file permission enforcement on non-Windows (chmod 600 fallback currently best-effort)

## 8. Configuration & Paths
- ✅ `CODEPUNK_CONFIG_HOME` override implemented (documented)
- ⏳ Command to print resolved config paths (`codepunk config paths`)
- ⏳ Hot reload of provider configuration without restart

## 9. Testing Strategy
- ✅ Added DI resolution test for interactive loop & renderer
- 🔄 Need scenario tests:
  - 🧪 `run` command: new session creation, `--continue`, `--session`, conflict of `--continue` + `--session`
  - 🧪 Agent override precedence (agent model vs `--model` flag)
  - 🧪 Models command output with authenticated vs unauthenticated state
  - 🧪 Auth / Agent command round-trip snapshot (create/list/show/delete)
  - 🧪 Root invocation with no args enters interactive mode (detect via injected test console abstraction)
- ⏳ Provider missing key error path tests
- ⏳ Performance regression micro-benchmarks (streaming throughput)

## 10. Refactors / Tech Debt
- 🧹 Extract Program.cs service registrations into `AddCodePunkConsole()` extension
- 🧹 Introduce `IInteractiveChatLoop` interface (simplify mocking / test harness)
- 🧹 Collapse duplicated test host bootstrapping into shared factory
- 🧹 Consolidate file store persistence patterns (tmp + atomic move) into utility
- 🧹 Replace random ID generation scheme with ULID for chronological ordering

## 11. Performance
- ⏳ Streaming renderer backpressure handling (avoid console flicker / batching)
- ⏳ Parallel tool execution when multiple calls returned
- ⏳ Cache recent session messages in memory to reduce DB round-trips

## 12. Documentation
- 🔄 README updates (CLI commands, config paths, interactive commands)
- ⏳ Add architecture diagram (components & flows)
- ⏳ Provider integration guide template
- ⏳ Troubleshooting section (common errors: missing key, network, model not found)

## 13. Future Features (Exploratory)
- ⏳ Multi-agent orchestrations (specialist agents pipeline)
- ⏳ Background watch mode (auto-summarize recent git changes)
- ⏳ Inline diff apply / revert for AI-generated patches
- ⏳ Web UI (shared sessions & collaboration) optional

## 14. Quality Gates & Metrics
- ⏳ Enforce minimum test count delta for PRs (CI rule)
- ⏳ Collect token usage & cost estimates (if pricing metadata configured)

---

### Immediate Next Sprint Candidates
1. Replace `Task.Delay(1)` with event-driven processing state (Chat session stabilization)
2. Real model listing + provider key validation in `models` command
3. `run` command scenario & conflict tests
4. Auth / Agent snapshot tests
5. Refactor DI registrations into extension method

### Notes
- Current test stats: 93 total (92 passing, 1 skipped) after DI / renderer registration fix.
- Temporary heuristics: token estimation via char/4; upgrade to tokenizer libs later.

Feel free to append inline decisions or sign off on completed items using initials + date.

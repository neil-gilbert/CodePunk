# CodePunk Roadmap / Remaining Work

Status legend: ✅ done · 🔄 in-progress · ⏳ planned · 🧪 needs tests · 🧹 refactor · ❗ open decision

## 1. CLI & UX
- ✅ Enhance `models` command: enumerate real models (dynamic fetch + fallback) & show key presence (9 Sep 2025)
- 🧪 Add `--json` output option for `run`, `auth list`, `agent list` (partial: `models` already supports) 
- ✅ Graceful error when no providers / API keys configured (guidance message implemented)
- ⏳ Root-level `sessions` management (list, show, load) mirrored from interactive commands
- ⏳ Add `--provider` / `--model` flags to `run` that override agent defaults (already partially supported internally, needs docs & tests)
- ⏳ Interactive: command autocompletion (tab) & history persistence

## 2. Chat / Session Core
- ✅ Replaced artificial `Task.Delay(1)` with channel-based event stream (MessageStart/Complete, ToolIteration*, StreamDelta) (9 Sep 2025)
- ⏳ Session pruning / archive strategy (size limits, rotation)
- ⏳ Export session to markdown / JSON command
- ⏳ Import session from JSON file

## 3. Providers & Models
- 🧪 Azure OpenAI provider implementation (pending)
- ⏳ Local model provider(s): Ollama + LM Studio
- ⏳ Dynamic provider discovery via configuration section scanning
- ✅ Model capability metadata surfaced (context/max/tools/streaming columns + JSON) (9 Sep 2025)

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
- ✅ Models command auth state tests (hasKey, filter, JSON hasKey) (9 Sep 2025)
- ✅ Event stream ordering & streaming delta tests (9 Sep 2025)
- 🔄 Remaining scenario tests:
  - 🧪 `run` command: new session creation, `--continue`, `--session`, conflict handling
  - 🧪 Agent override precedence (agent model vs `--model` flag)
  - 🧪 Auth / Agent round-trip snapshot
  - 🧪 Root invocation no-args interactive detection
- ⏳ Provider missing key error path tests
- ⏳ Performance regression micro-benchmarks (streaming throughput)

## 10. Refactors / Tech Debt
- ✅ Extracted Program.cs service registrations into `AddCodePunkConsole()` extension (9 Sep 2025)
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
1. `run` command scenario & conflict tests
2. Agent override precedence tests
3. Auth / Agent snapshot tests
4. Config paths command
5. Provider missing key error tests & Azure OpenAI provider spike

### Notes
- Current test stats: 108 total (107 passing, 1 skipped) after event stream + models + DI refactor.
- Temporary heuristics: token estimation via char/4; upgrade to tokenizer libs later.
- Channel event stream now source of truth for processing state; future UI can subscribe.

Feel free to append inline decisions or sign off on completed items using initials + date.

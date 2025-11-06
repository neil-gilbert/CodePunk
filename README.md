<picture>
  <source media="(prefers-color-scheme: dark)" srcset="images/codepunk-dark.png">
  <img alt="CodePunk 8-bit pixel art logo" src="images/codepunk-light.png" width=400 />
</picture>

# CodePunk

> **Note**: This is a test edit to verify prompt caching functionality.
**Your new coding bestie, now available in your favourite terminal.** Your tools, your code, and your workflows, wired into your LLM of choice.

[![Latest Release](https://img.shields.io/github/v/release/neil-gilbert/CodePunk?include_prereleases&style=for-the-badge&logo=github)](https://github.com/neil-gilbert/CodePunk/releases)
[![Build Status](https://img.shields.io/github/actions/workflow/status/neil-gilbert/CodePunk/release.yml?branch=main&style=for-the-badge&logo=githubactions)](https://github.com/neil-gilbert/CodePunk/actions)
[![License](https://img.shields.io/github/license/neil-gilbert/CodePunk?style=for-the-badge)](https://github.com/neil-gilbert/CodePunk/blob/main/LICENSE)

CodePunk is an intelligent, agentic coding companion built for engineers. It lives in your terminal, integrates with your tools, and connects to any AI provider to give you context-aware assistance without leaving your command line.

## Features

*   **Multi-Model**: Choose from a wide range of LLMs (OpenAI, Anthropic, etc.) or add your own via compatible APIs.
*   **Flexible**: Switch LLMs mid-session while preserving context.
*   **Session-Based**: Maintain multiple work sessions and contexts per project.
*   **Tool-Enabled**: Natively uses your shell, reads/writes files, and can be extended with new tools.
*   **Works Everywhere**: First-class support on macOS, Linux, and Windows (PowerShell and WSL).
*   **Automation-Friendly**: Quiet/JSON mode (`--json`) for clean, scriptable output.

## Installation

The easiest way to install CodePunk is as a .NET global tool.

**Prerequisites**: [.NET 9.0 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)

```bash
# Install the tool from NuGet
dotnet tool install --global CodePunk --prerelease

# Start using it
codepunk --help
```

You can also download pre-built, self-contained binaries from the [Releases](https://github.com/neil-gilbert/CodePunk/releases) page.

| RID           | Architecture | Download Example                        |
|---------------|--------------|-----------------------------------------|
| `linux-x64`   | Linux x64    | `codepunk-v0.1.0-alpha.2-linux-x64.zip`   |
| `linux-arm64` | Linux ARM64  | `codepunk-v0.1.0-alpha.2-linux-arm64.zip` |
| `osx-x64`     | macOS x64    | `codepunk-v0.1.0-alpha.2-osx-x64.zip`     |
| `osx-arm64`   | macOS ARM64  | `codepunk-v0.1.0-alpha.2-osx-arm64.zip`   |
| `win-x64`     | Windows x64  | `codepunk-v0.1.0-alpha.2-win-x64.zip`     |

An experimental Native AOT build for `linux-x64` is also available for faster startup.

## Getting Started

The quickest way to get started is to run the interactive setup inside the app (no environment vars required).

```bash
codepunk          # then type /setup in interactive mode
```

You will be guided to:

1. Pick a provider (Anthropic for now; additional providers coming) even if no keys are yet configured
2. Paste your API key (stored locally in a secure JSON file: `auth.json` with chmod 600 on *nix)
3. Pick a default model (if multiple)

After that you can immediately chat, list models, or create plans. If you add keys outside the app later, use `/reload` to dynamically re-scan and register providers without restarting.

### Modes (First Turn)

On the first message of a new session, CodePunk now asks the model to classify and activate one mode by calling a tool:

- PLAN: `mode_plan` – for new work/features; typically followed by `plan_generate_ai` to draft a multi-file plan.
- BUG: `mode_bug` – for triage and fixing issues; suggests realistic next steps (read/search/shell/tests).

After activation, the model proceeds with developer-style steps using existing tools (read_file, glob, search_file_content, shell, write_file).

### Optional: Environment Variables (Legacy / CI Friendly)

You can still export keys for non-interactive or CI usage:

| Environment Variable  | Provider   |
|-----------------------|------------|
| `OPENAI_API_KEY`      | OpenAI     |
| `ANTHROPIC_API_KEY`   | Anthropic  |

Once your key (or stored credential) is set, you can start a new chat session:

```bash
codepunk chat "How can I recursively find all *.js files in a directory?"
```

## Configuration

CodePunk works great with no configuration. However, you can customize settings by creating a `codepunk.json` file in your project directory or a global config at `~/.config/codepunk/codepunk.json`.

*Coming soon: Detailed configuration options for providers, tools, and session management.*

### Persisted State Files

| File | Purpose | Location (Unix) | Notes |
|------|---------|-----------------|-------|
| `auth.json` | Stored provider API keys | `~/.config/codepunk/` | Created via `/setup` or `auth login`; chmod 600 attempted |
| `defaults.json` | Last selected provider + model | `~/.config/codepunk/` | Loaded on startup to set session defaults |
| `codepunk.db` | Chat/session/plan persistence (SQLite) | App working dir | Auto-created |

You may delete these to fully reset local state. On next launch, run `/setup` again.

### Dynamic Provider Bootstrap

Providers are registered at startup from (in order):
1. Environment variables (`OPENAI_API_KEY`, `ANTHROPIC_API_KEY`)
2. `appsettings*.json` configuration values
3. Persisted credentials in `auth.json`

If you add a key after launch, run `/reload` to apply it without restarting.

## Building from Source

If you want to build from source, you'll need the [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
# Clone the repository
git clone https://github.com/neil-gilbert/CodePunk.git
cd CodePunk

# Restore dependencies and build
dotnet build

# Run the console app
dotnet run --project src/CodePunk.Console -- --help
```

## What's the vibe?

We'd love to hear your thoughts on this project. Find a bug? Have a feature idea? Feel free to open an issue or join the discussion.

---

Part of the **CodePunk** ecosystem. Built with ❤️ for developers.

## Why CodePunk?

- **Universal Language Support**: Works with Python, JavaScript, Go, Rust, Java, C#, and any codebase
- **Provider Agnostic**: Switch between OpenAI GPT-4o, Anthropic Claude, local models, or any AI provider
- **Tool Integration**: Execute shell commands, read/write files, and interact with your development environment
- **Session Persistence**: Never lose context - all conversations and file changes are tracked
- **Built for Engineers**: No black boxes, full transparency, and designed for technical workflows
- Quiet / JSON mode: many commands support machine-friendly output. Use `--json` or set `CODEPUNK_QUIET=1` to suppress decorative output and emit a single JSON payload for automation.

##  Quick Start

### Installation

**Prerequisites**: [.NET 9.0 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (the tool runs anywhere .NET runs)

```bash
# Clone and build
git clone https://github.com/neil-gilbert/CodePunk.git
dotnet build

# Configure your AI provider
export OPENAI_API_KEY="your-key-here"
# OR
export ANTHROPIC_API_KEY="your-key-here"

# Run CodePunk
dotnet run --project src/CodePunk.Console
```

### Distribution (Pre-built & Tool Install)

Once releases are published you can install the global tool (preferred):

```bash
dotnet tool install -g CodePunk --prerelease   # omit --prerelease for stable
codepunk --help
```

Upgrade:

```bash
dotnet tool update -g CodePunk
```

Uninstall:

```bash
dotnet tool uninstall -g CodePunk
```

### Self-Contained Single-File Binaries

Releases also ship zipped single-file binaries for common platforms:

| RID | File Example |
|-----|--------------|
| linux-x64 | `codepunk-v0.3.0-linux-x64.zip` |
| linux-arm64 | `codepunk-v0.3.0-linux-arm64.zip` |
| osx-x64 | `codepunk-v0.3.0-osx-x64.zip` |
| osx-arm64 | `codepunk-v0.3.0-osx-arm64.zip` |
| win-x64 | `codepunk-v0.3.0-win-x64.zip` |

Usage (Linux/macOS):

```bash
unzip codepunk-v0.3.0-linux-x64.zip -d codepunk
cd codepunk
chmod +x codepunk
./codepunk --help
```

Usage (Windows):

```powershell
Expand-Archive codepunk-v0.3.0-win-x64.zip -DestinationPath codepunk
cd codepunk
./codepunk.exe --help
```

### Native AOT (Experimental)

Experimental AOT builds (beginning with `linux-x64`) are published with `-aot` suffix, e.g. `codepunk-v0.3.0-linux-x64-aot.zip`.

Pros:
- Faster cold start
- Smaller memory footprint

Trade-offs:
- Larger build times
- Some dynamic logging/telemetry features may be reduced

Verify checksum (macOS/Linux):

```bash
shasum -a 256 codepunk-v0.3.0-linux-x64.zip | grep <expected>
```

### Embedding Commit / Version

`codepunk --version` prints semantic version and short commit SHA for reproducibility.

### First Session

```bash
> codepunk

# Start coding with AI assistance
> Analyze this Python file and suggest optimizations
> /file myapp.py

# Execute shell commands through AI
> Run the test suite and analyze any failures

# Get context-aware help for any language
> Explain this Kubernetes deployment and suggest improvements
> /file k8s-deployment.yaml
> /reload   # (only needed if you added keys manually mid-session)
```

## 🔌 AI Provider Support

CodePunk works with multiple AI providers through a unified interface:

### Supported Providers
- **OpenAI**: GPT-4.1, GPT-4.1-mini, GPT-4o, GPT-4o-mini, GPT-3.5-turbo (legacy)
- **Anthropic**: Claude Opus 4.1, Claude Opus 4, Claude Sonnet 4, Claude Sonnet 3.7, Claude Haiku 3.5
- **Local Models**: Ollama, LM Studio integration *(coming soon)*
- **Azure OpenAI**: Enterprise deployments *(coming soon)*

### Provider Configuration
```json
{
  "AI": {
    "DefaultProvider": "anthropic",
    "Providers": {
      "OpenAI": {
        "ApiKey": "your-openai-key",
        "DefaultModel": "gpt-4o",
        "MaxTokens": 4096,
        "Temperature": 0.7
      },
      "Anthropic": {
        "ApiKey": "your-anthropic-key", 
        "DefaultModel": "claude-3-5-sonnet-20241022",
        "MaxTokens": 4096,
        "Temperature": 0.7,
        "Version": "2023-06-01"
      }
    }
  }
}
```

### Environment Variables (Recommended)
```bash
# OpenAI
export OPENAI_API_KEY="your-openai-key-here"

# Anthropic  
export ANTHROPIC_API_KEY="your-anthropic-key-here"

# Set default provider in appsettings.json or switch dynamically
```

### Runtime Reload

At any time in interactive mode:

```bash
/reload   # Re-scan env/config/auth.json and register newly available providers
```

### Provider Comparison

| Feature | OpenAI GPT-4o | Anthropic Claude 3.5 |
|---------|---------------|----------------------|
| **Context Window** | 128K tokens | 200K tokens |
| **Best For** | Complex reasoning, detailed analysis | Code review, concise explanations |
| **Streaming** | ✅ | ✅ |
| **Tool Calling** | ✅ | ✅ |
| **Cost (Input)** | $2.50/1M tokens | $3.00/1M tokens |
| **Cost (Output)** | $10.00/1M tokens | $15.00/1M tokens |
| **Response Style** | Verbose, comprehensive | Concise, focused |

## 🛠️ Core Features

### Agentic Capabilities
- **Tool Execution**: AI can read files, execute commands, and modify your codebase
- **Context Awareness**: Understands project structure across any language or framework  
- **Session Memory**: Persistent conversations with full history and file tracking
- **Streaming Responses**: Real-time AI output with immediate feedback

### Developer Integration
- **Shell Command Execution**: Run tests, build scripts, git operations through AI
- **File Operations**: Read, write, and analyze files in any programming language
- **Project Understanding**: Works with Python projects, Node.js apps, Go modules, Rust crates, etc.
- **Error Analysis**: Intelligent debugging across different tech stacks

### Multi-Language Support
- **Python**: Virtual environments, pip, pytest, Django, Flask
- **JavaScript/TypeScript**: npm, yarn, Node.js, React, Vue, Angular  
- **Go**: Modules, testing, build tools
- **Rust**: Cargo, crates, testing
- **Java**: Maven, Gradle, Spring Boot
- **C#**: NuGet, MSBuild, .NET projects
- **And more**: Works with any language or framework

## 🏗️ Architecture

CodePunk is built with clean architecture principles, making it extensible and maintainable:

### Core Components
- **Provider Abstraction**: Unified interface for any AI provider
- **Tool System**: Extensible framework for AI-executable actions
- **Session Management**: Persistent conversation and context tracking
- **Terminal Interface**: Rich console experience with Spectre.Console

### Technology Stack
- **Runtime**: .NET 9.0 (cross-platform: Windows, macOS, Linux)
- **Database**: SQLite for local persistence
- **HTTP**: Modern async HTTP client for AI provider communication
- **UI**: Spectre.Console for rich terminal experiences
- **Testing**: Comprehensive test suite with 100% core coverage

## 🌟 Use Cases

### Provider-Specific Strengths

**OpenAI GPT-4o**: Excellent for complex reasoning, code generation, and detailed analysis
```bash
> /provider openai
> Analyze this entire codebase and suggest architectural improvements
> /file src/
```

**Anthropic Claude**: Superior for code review, concise explanations, and safety-conscious responses
```bash
> /provider anthropic
> Review this code for security vulnerabilities and provide brief, actionable fixes
> /file auth.py
```

### Code Review & Analysis
```bash
> Analyze this codebase for security vulnerabilities
> /file src/
> Focus on authentication and data validation
```

### Debugging Across Languages
```bash
> My Python tests are failing, help me debug
> /shell pytest -v
> Analyze the output and suggest fixes
```

### Architecture & Design
```bash
> Review this microservices architecture  
> /file docker-compose.yml
> /file k8s/
> Suggest improvements for scalability
```

### Refactoring & Optimization
```bash
> Refactor this JavaScript code to use modern ES6+ features
> /file legacy-code.js
> Maintain backward compatibility
```

### Dynamic Provider Switching
```bash
# Switch to Anthropic for concise code review
> /provider anthropic
> Review this function for bugs - be brief and specific
> /file utils.py

# Switch to OpenAI for detailed architecture discussion  
> /provider openai
> Explain the design patterns used in this codebase and suggest improvements
> /file src/
```

## 🤝 Contributing

CodePunk is open source and welcomes contributions from engineers working in any language:

### Development Setup
```bash
git clone https://github.com/neil-gilbert/CodePunk.git
cd CodePunk
dotnet restore
dotnet test  # Ensure tests pass (current: 167 passing, 2 skipped)
```

## 🧭 CLI Command Reference

Current top-level commands (invoke with `codepunk <command>` or `dotnet run --project src/CodePunk.Console -- <command>`):

| Command | Description | Key Options |
|---------|-------------|-------------|
| `run [message]` | One-shot prompt or interactive if no message | `--session <id>`, `--continue`, `--agent <name>`, `--model <provider/model>` |
| `auth login` | Store provider API key | `--provider <name>` `--key <value>` |
| `auth list` | List providers with stored keys | *(none)* |
| `auth logout` | Remove stored key | `--provider <name>` |
| `agent create` | Create an agent (named defaults & prompt) | `--name --provider --model --prompt-file --overwrite` |
| `agent list` | List agents | *(none)* |
| `agent show` | Show agent definition (JSON) | `--name <agent>` |
| `agent delete` | Delete agent | `--name <agent>` |
| `models` | List provider models (table or JSON) | `--json`, `--available-only` |
| `reload` | Dynamically reload provider registrations | *(none)* |
| `sessions list` | List recent sessions (table or JSON) | `--take <n>`, `--json` |
| `sessions show` | Show a session transcript | `--id <sessionId>`, `--json` |
| `sessions load` | Mark a session as the active context (prints status) | `--id <sessionId>` |
| `plan create` | Create a new plan | `--goal <text>` |
| `plan list` | List recent plans | `--take <n>`, `--json` |
| `plan show` | Show full plan JSON | `--id <planId>` |
| `plan add` | Stage file change or deletion | `--id <planId> --path <file> [--after-file <file>] [--rationale <text>] [--delete] [--json]` |
| `plan diff` | Show unified diffs for staged changes | `--id <planId> [--json]` |
| `plan apply` | Apply changes (drift-safe) | `--id <planId> [--dry-run] [--force] [--json]` |
| `plan generate --ai` | AI-generate multi-file change plan | `--goal <text> [--provider <p>] [--model <m>] [--json]` |

Invoking with no command launches the interactive chat loop.

### JSON Output (Schemas)

All automation-friendly output includes a `schema` field. Current schemas:

| Area | Command | Schema |
|------|---------|--------|
| Run | `run --json` | `run.execute.v1` |
| Sessions | `sessions list --json` | `sessions.list.v1` |
| Sessions | `sessions show --id <id> --json` | `sessions.show.v1` |
| Plan | `plan create --goal .. --json` | `plan.create.v1` |
| Plan | `plan add --json` | `plan.add.v1` |
| Plan | `plan diff --json` | `plan.diff.v1` |
| Plan | `plan show --json` | `plan.show.v1` |
| Plan | `plan apply --json` | `plan.apply.v1` |
| Plan | `plan generate --ai --json` | `plan.generate.ai.v1` |
| Models | `models --json` | `models.list.v1` |

Backward-compatible changes may add new fields; the `schema` value will change for breaking updates only.

#### Run Command Example

```bash
codepunk run "Summarize this file" --session new --json
```

```json
{
  "schema": "run.execute.v1",
  "sessionId": "20250913-abcdef",
  "agent": "default",
  "model": "anthropic/claude-3-5-sonnet",
  "request": "Summarize this file",
  "response": "Here is a concise summary...",
  "usage": { "promptChars": 1200, "completionChars": 560, "approxPromptTokens": 300, "approxCompletionTokens": 140 }
}
```

#### Sessions Example

List:
```json
{ "schema": "sessions.list.v1", "sessions": [ { "id": "20250913-ab12cd", "createdUtc": "2025-09-13T10:45:12Z", "title": "Refactor planner" } ] }
```

Show:
```json
{ "schema": "sessions.show.v1", "session": { "id": "20250913-ab12cd", "messages": [ { "role": "user", "content": "Hello" }, { "role": "assistant", "content": "Hi!" } ] } }
```

#### Plan Add Examples

Stage modification:
```bash
plan add --id <planId> --path src/Foo.cs --after-file Foo.updated.cs --json
```
```json
{
  "schema": "plan.add.v1",
  "planId": "20250913-abc123",
  "file": {
    "path": "src/Foo.cs",
    "staged": true,
    "hasAfter": true,
    "isDelete": false,
    "hashBefore": "...",
    "hashAfter": "...",
    "diffGenerated": true
  },
  "rationale": "Refactor method names"
}
```

Stage deletion:
```bash
plan add --id <planId> --path src/OldFile.cs --delete --json
```
```json
{
  "schema": "plan.add.v1",
  "planId": "20250913-abc123",
  "file": {
    "path": "src/OldFile.cs",
    "staged": true,
    "hasAfter": false,
    "isDelete": true,
    "hashBefore": "...",
    "hashAfter": null,
    "diffGenerated": true
  },
  "rationale": null
}
```

#### Plan Diff Example
```json
{ "schema": "plan.diff.v1", "planId": "20250913-abc123", "diffs": [ { "path": "src/Foo.cs", "diff": "--- before\n+++ after\n..." }, { "path": "src/OldFile.cs", "diff": "Deletion staged for src/OldFile.cs" } ] }
```

#### Plan Apply Example
```json
{
  "schema": "plan.apply.v1",
  "planId": "20250913-abc123",
  "dryRun": false,
  "force": false,
  "summary": { "applied": 2, "skipped": 1, "drift": 1, "backedUp": 2 },
  "changes": [
    { "path": "src/Foo.cs", "action": "applied", "hadDrift": false, "backupPath": "/Users/me/.config/codepunk/plans/backups/..../Foo.cs" },
    { "path": "src/OldFile.cs", "action": "deleted", "hadDrift": false, "backupPath": "/Users/me/.config/codepunk/plans/backups/.../OldFile.cs" },
    { "path": "src/Bar.cs", "action": "skipped-drift", "hadDrift": true, "backupPath": null }
  ]
}
```

Possible per-file `action` values (v1):

- `applied` – File content written
- `dry-run` – Would be written (due to `--dry-run`)
- `deleted` – File deleted
- `dry-run-delete` – Would be deleted (dry-run)
- `skip-missing` – Deletion skipped (file no longer exists)
- `skipped-drift` – Skipped because original content changed (without `--force`)
- `skipped-error` – Error writing file
- `delete-error` – Error deleting file

Drift count in `summary` reflects files skipped for drift (whether or not `--force` was used). When `--force` is specified, drifted files still apply and `hadDrift` is `true` for those entries.

#### Models List Example
```bash
codepunk models --available-only --json
```
```json
{
  "schema": "models.list.v1",
  "models": [
    { "provider": "anthropic", "id": "claude-3-5-sonnet", "name": "Claude 3.5 Sonnet", "context": 200000, "maxTokens": 4096, "tools": true, "streaming": true, "hasKey": true }
  ]
}
```

### Interactive Slash Commands

Inside the chat loop you can use:

| Command | Purpose |
|---------|---------|
| `/help` | Show available slash commands |
| `/new` | Start a new session |
| `/sessions` | List recent sessions (interactive view) |
| `/load <sessionId>` | Load a prior session |
| `/clear` | Clear current session and start fresh |
| `/quit` | Exit the interactive loop |
| `/plan` | Access plan subcommands inside chat (e.g. `/plan create --goal "Refactor API"`, `/plan add --id <id> --path src/Foo.cs --json`, `/plan apply --id <id> --dry-run`) |
| `/plan generate --ai --goal "Improve logging"` | Generate an initial multi-file plan from a natural language goal |

Planned (not yet implemented): `/provider <name>`, `/model <id>`, `/export <path>`, `/import <path>`.

#### Using `/plan` interactively

Plans allow you to stage, review, and apply file changes systematically. You can manage plans without leaving the chat loop:

**Basic workflow:**
1. **Create a plan** with a goal description
2. **Stage files** - capture current state and optionally provide updated content 
3. **Review changes** with unified diffs
4. **Apply safely** with drift detection and automatic backups

**Examples:**

```text
# Create a new plan for refactoring
/plan create --goal "Refactor authentication to use dependency injection"

# Stage a file with before/after changes
/plan add --id 20250912123000-abc123 --path src/AuthService.cs --after-file AuthService.updated.cs

# Review what changes will be applied
/plan diff --id 20250912123000-abc123

# Test run without making changes
/plan apply --id 20250912123000-abc123 --dry-run

# Apply changes with automatic backup
/plan apply --id 20250912123000-abc123
```

**Key features:**
- **Drift detection**: Won't apply if files changed since staging
- **Automatic backups**: All original files saved before changes
- **JSON output**: Add `--json` for automation-friendly results
- **Dry run**: Preview changes with `--dry-run` flag
- **Force apply**: Override drift detection with `--force`

### AI Plan Generation (Phase 2)

Use `plan generate --ai --goal "<goal text>"` to have the configured AI provider propose a multi-file plan. The model returns a JSON object containing a `files` array. Each entry becomes a `PlanFileChange` with:
 - `path`: Target relative file path
 - `action`: `modify` (default) or `delete`
 - `rationale`: Short explanation (may be truncated for safety)

Example:
```bash
codepunk plan generate --ai --goal "Introduce structured logging and remove obsolete LoggerUtil class" --json
```
```json
{
  "schema": "plan.generate.ai.v1",
  "planId": "20250914-abcd12",
  "goal": "Introduce structured logging and remove obsolete LoggerUtil class",
  "provider": "anthropic",
  "model": "claude-3-5-sonnet",
  "files": [
    { "path": "src/Infrastructure/Logging/StructuredLogger.cs", "action": "modify", "rationale": "Add wrapper around existing logger with structured context" },
    { "path": "src/Legacy/LoggerUtil.cs", "action": "delete", "rationale": "Deprecated after structured logger introduction", "diagnostics": ["UnsafePath"] }
  ],
  "generation": {
    "provider": "anthropic",
    "model": "claude-3-5-sonnet",
    "promptTokens": 812,
    "completionTokens": 210,
    "totalTokens": 1022,
    "iterations": 1,
    "safetyFlags": ["TruncatedContent"],
    "createdUtc": "2025-09-14T10:10:10Z"
  }
}
```

Note: Field values above are illustrative. Safety flags and token usage appear only when available.

#### Safety & Diagnostics

AI-generated plans undergo validation:
 - Path validation: rejects rooted paths or `..` segments (flag: `UnsafePath`)
 - File count cap: configurable `MaxFiles` (returns error `TooManyFiles` if exceeded)
 - Rationale truncation: per-file (`TruncatedContent`) and aggregate size (`TruncatedAggregate`) limits (UTF-8 safe)
 - Secret pattern redaction: replaces configured patterns with `<REDACTED>` (`SecretRedacted`)
 - JSON retry: configurable retries (`RetryInvalidOutput`) for malformed model output (error `ModelOutputInvalid` if persistent)

Per-file diagnostics are listed under each file as `diagnostics`; aggregated unique flags appear in `generation.safetyFlags`.

#### Configuration (`PlanAI` section)

Configure generation limits in `appsettings.json` (or environment overlay):
```json
{
  "PlanAI": {
    "MaxFiles": 20,
    "MaxPathLength": 260,
    "MaxPerFileBytes": 16384,
    "MaxTotalBytes": 131072,
    "RetryInvalidOutput": 1,
    "SecretPatterns": ["api_key=", "BEGIN PRIVATE KEY"]
  }
}
```

Flags: `--provider` and `--model` override defaults for a single generation. Omit `--json` for a human-readable table; include it for automation.

Error Codes (JSON): `ModelUnavailable`, `ModelOutputInvalid`, `TooManyFiles`.

Token usage (if available from the provider) is captured in `generation.promptTokens|completionTokens|totalTokens`.

All flags and behaviors are identical to the top-level `plan` CLI group. Perfect for iterative development where you want to plan changes, discuss with AI, then apply systematically.

### Configuration Paths

Default config is stored under:

- macOS/Linux: `~/.config/codepunk/`
- Windows: `%APPDATA%/CodePunk/`

Override with environment variable:
```bash
export CODEPUNK_CONFIG_HOME="/custom/path/codepunk-config"
```

Files & directories:
| Path | Purpose |
|------|---------|
| `auth.json` | Stored provider API keys (masked in listings) |
| `agents/` | Agent definition JSON files |
| `sessions/` | Individual session transcripts |
| `sessions/index.json` | Session metadata index (recency sorting) |
| `plans/` | Plan JSON documents |
| `plans/index.json` | Plan definitions index |

### Recent Internal Changes

- Extracted root command construction into `RootCommandFactory` for testability.
- Added default root handler (launch interactive when no args supplied).
- Refactored `HelpCommand` to two-stage initialization to avoid DI circular dependency.
- Added `StreamingResponseRenderer` and ensured DI registration (fixing runtime activation error).
- Introduced DI resolution test and file store robustness (`EnsureCreated` in index update path).
- Stabilized processing state tests with temporary async yield + minimal delay (will evolve to event-driven model).
- Added sessions root CLI (`sessions list|show|load`) with JSON output for automation.
- Added models command JSON output and `--available-only` flag (filters to providers with stored API keys).
- Introduced plan subsystem (create/list/show/add/diff/apply) with staging, unified diffs, drift detection (`plan apply --force` to override) and dry-run support.
 - Added deletion staging (`plan add --delete`) and safe deletion handling with backups in `plan apply`.
 - Standardized JSON schemas across run, sessions, and plan operations with centrally defined constants.

See `ROADMAP.md` for upcoming enhancements (model listing, provider extensions, improved tool system).

### Open Backlog Items

The following technical tasks were identified but not completed in this change set:

- Add Spectre.Console output capture helper for asserting error/markup content in CLI scenario tests.
- Add test covering `ChatSessionEventType.ToolLoopExceeded` (tool loop exceeded event) in streaming and non-stream paths.
- Consolidate `TrimTitle` logic (currently implemented separately in `NewCommand` and `RootCommandFactory`).
- Extract a shared test host factory for Core chat unit tests (similar to `ConsoleTestHostFactory`).
- Introduce streaming fallback test verifying max tool iteration fallback in streaming API (`SendMessageStreamAsync`).
- Eliminate remaining analyzer warnings (e.g. nullable dereference in `NewCommandTests`, any residual async warnings).
- Provide documentation for run command agent/model override precedence and tool iteration/fallback behavior.
- Optional: Add quiet/diagnostic toggle to reduce console noise during test runs.

### Areas for Contribution
- **AI Provider Integration**: Add local models, Azure OpenAI, or other providers
- **Tool Development**: Create tools for specific languages or frameworks
- **Language Support**: Enhance support for specific programming languages
- **Performance**: Optimize response times and memory usage
- **Documentation**: Examples, tutorials, and integration guides

### Adding New Providers
The provider system is designed for easy extensibility:

```csharp
public class NewProvider : ILLMProvider
{
    public string Name => "NewProvider";
    public IReadOnlyList<LLMModel> Models { get; }
    
    public async Task<LLMResponse> SendAsync(LLMRequest request, 
        CancellationToken cancellationToken = default)
    {
        // Implementation for new provider API
    }
    
    public IAsyncEnumerable<LLMStreamChunk> StreamAsync(LLMRequest request, 
        CancellationToken cancellationToken = default)
    {
        // Streaming implementation
    }
}
```

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

Built for the engineering community using:
- [.NET 9.0](https://dotnet.microsoft.com/) - Cross-platform runtime
- [Spectre.Console](https://spectreconsole.net/) - Rich terminal experiences  
- [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/) - Data persistence
- The open-source community for inspiration and contributions

---

**CodePunk** - Agentic coding assistance for engineers, by engineers.

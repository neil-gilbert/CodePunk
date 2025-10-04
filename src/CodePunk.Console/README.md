<picture>
  <source media="(prefers-color-scheme: dark)" srcset="images/codepunk-dark.png">
  <img alt="CodePunk 8-bit pixel art logo" src="images/codepunk-light.png" width=400 />
</picture>

# CodePunk

**Your coding companion in the terminal.** AI-powered development assistance across languages and tools.

[![Latest Release](https://img.shields.io/github/v/release/neil-gilbert/CodePunk?include_prereleases&style=for-the-badge&logo=github)](https://github.com/neil-gilbert/CodePunk/releases)
[![Build Status](https://img.shields.io/github/actions/workflow/status/neil-gilbert/CodePunk/release.yml?branch=main&style=for-the-badge&logo=githubactions)](https://github.com/neil-gilbert/CodePunk/actions)
[![License](https://img.shields.io/github/license/neil-gilbert/CodePunk?style=for-the-badge)](https://github.com/neil-gilbert/CodePunk/blob/main/LICENSE)

## Quick Start

**Prerequisites**: [.NET 9.0 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)

```bash
# Install the global tool
dotnet tool install --global CodePunk --prerelease

# Start using it
codepunk --help
```

## Key Features

- **Multi-Model Support**: OpenAI, Anthropic, and more
- **Cross-Platform**: Works on macOS, Linux, and Windows
- **Session-Based**: Maintain context across conversations
- **Tool-Enabled**: Execute shell commands, read/write files
- **Automation-Friendly**: JSON output for scripting

## Basic Usage

```bash
# Interactive chat
codepunk

# One-shot prompt
codepunk chat "Help me refactor this Python function"

# Run with a specific provider/model
codepunk run "Explain this code" --provider anthropic --model claude-3-5-sonnet
```

## Configuration

Configure providers via environment variables or interactive setup:

```bash
# Set API keys
export OPENAI_API_KEY="your-key"
export ANTHROPIC_API_KEY="your-key"

# Or use interactive setup
codepunk
> /setup
```

## Supported Providers

- OpenAI: GPT-4o, GPT-4o-mini
- Anthropic: Claude Opus, Claude Sonnet, Claude Haiku
- Local models (coming soon)

## Contributing

```bash
git clone https://github.com/neil-gilbert/CodePunk.git
cd CodePunk
dotnet restore
dotnet test
```

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Why CodePunk?

CodePunk is built for engineers who want an intelligent, context-aware coding assistant directly in their terminal. Switch models, maintain session context, and integrate with your existing workflows.
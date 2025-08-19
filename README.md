# CodePunk

**An agentic coding assistant that works with your existing tools and any AI provider.**

CodePunk is an intelligent coding companion built for engineers working in any language or framework. Whether you're debugging Python, refactoring JavaScript, analyzing Go, or architecting distributed systems, CodePunk provides context-aware assistance through a powerful terminal interface.

## Why CodePunk?

- **Universal Language Support**: Works with Python, JavaScript, Go, Rust, Java, C#, and any codebase
- **Provider Agnostic**: Switch between OpenAI GPT-4o, Anthropic Claude, local models, or any AI provider
- **Tool Integration**: Execute shell commands, read/write files, and interact with your development environment
- **Session Persistence**: Never lose context - all conversations and file changes are tracked
- **Built for Engineers**: No black boxes, full transparency, and designed for technical workflows

##  Quick Start

### Installation

**Prerequisites**: [.NET 9.0 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (the tool runs anywhere .NET runs)

```bash
# Clone and build
git clone https://github.com/charmbracelet/crush
cd crush/CodePunk.NET
dotnet build

# Configure your AI provider
export OPENAI_API_KEY="your-key-here"
# OR export ANTHROPIC_API_KEY="your-key-here"

# Run CodePunk
dotnet run --project src/CodePunk.Console
```

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
```

## 🔌 AI Provider Support

CodePunk works with multiple AI providers through a unified interface:

### Supported Providers
- **OpenAI**: GPT-4o, GPT-4o-mini, GPT-3.5-turbo
- **Anthropic**: Claude 3.5 Sonnet, Claude 3 Haiku *(coming soon)*
- **Local Models**: Ollama, LM Studio integration *(coming soon)*
- **Azure OpenAI**: Enterprise deployments *(coming soon)*

### Provider Configuration
```json
{
  "LLM": {
    "DefaultProvider": "OpenAI",
    "Providers": {
      "OpenAI": {
        "ApiKey": "your-openai-key",
        "DefaultModel": "gpt-4o"
      },
      "Anthropic": {
        "ApiKey": "your-anthropic-key", 
        "DefaultModel": "claude-3-5-sonnet"
      }
    }
  }
}
```

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

## 📊 Current Status

### ✅ Phase 1: Foundation Complete
- **Clean Architecture**: Modular design with clear separation of concerns
- **Data Persistence**: SQLite with Entity Framework Core for session management
- **Rich Terminal UI**: Spectre.Console interface with real-time streaming
- **Comprehensive Testing**: 57/57 tests passing with full coverage

### ✅ Phase 2: LLM Integration Complete  
- **Multi-Provider Support**: OpenAI GPT-4o, GPT-4o-mini, GPT-3.5-turbo
- **Streaming Responses**: Real-time AI output with `IAsyncEnumerable`
- **Tool Execution**: AI can read files, execute shell commands, modify code
- **Cost Tracking**: Token usage and cost calculation

### ✅ Phase 3: Interactive Chat Complete
- **Session Persistence**: Full conversation history and context management
- **File Tracking**: Associate code changes with specific conversations
- **Command System**: Built-in commands for session management and tool execution
- **Error Handling**: Robust error recovery and user feedback

### 🚧 Coming Next
- **Anthropic Claude Integration**: Claude 3.5 Sonnet support
- **Local Model Support**: Ollama and LM Studio integration  
- **Enhanced Tools**: Git operations, database queries, API testing
- **LSP Integration**: Language server protocol for intelligent code analysis

## 🌟 Use Cases

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

## 🤝 Contributing

CodePunk is open source and welcomes contributions from engineers working in any language:

### Development Setup
```bash
git clone https://github.com/charmbracelet/crush
cd crush/CodePunk.NET
dotnet restore
dotnet test  # Ensure all 57 tests pass
```

### Areas for Contribution
- **AI Provider Integration**: Add Anthropic, local models, or other providers
- **Tool Development**: Create tools for specific languages or frameworks
- **Language Support**: Enhance support for specific programming languages
- **Performance**: Optimize response times and memory usage
- **Documentation**: Examples, tutorials, and integration guides

### Adding New Providers
```csharp
public class AnthropicProvider : ILLMProvider
{
    public string Name => "Anthropic";
    
    public async Task<LLMResponse> SendAsync(LLMRequest request, 
        CancellationToken cancellationToken = default)
    {
        // Implementation for Claude API
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

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="images/codepunk-dark.png">
  <img alt="CodePunk 8-bit pixel art logo" src="images/codepunk-light.png" width=400 />
</picture>

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
# OR
export ANTHROPIC_API_KEY="your-key-here"

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
- **Anthropic**: Claude 3.5 Sonnet, Claude 3.5 Haiku, Claude 3 Opus, Claude 3 Sonnet, Claude 3 Haiku
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
git clone https://github.com/charmbracelet/crush
cd crush/CodePunk.NET
dotnet restore
dotnet test  # Ensure all 83 tests pass
```

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

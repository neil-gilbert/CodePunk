# CodePunk.NET 🤖✨

**CodePunk.NET** is a next-generation AI-powered coding assistant designed to revolutionize how developers interact with code, projects, and development workflows. Built with modern .NET architecture, it provides intelligent assistance for coding tasks, project management, and development automation.

## 🎯 Vision & Goals

CodePunk.NET aims to become the ultimate AI coding companion by providing:

- **Intelligent Code Assistance**: Context-aware code generation, refactoring, and optimization
- **Project Understanding**: Deep comprehension of your codebase structure and patterns  
- **Multi-Modal Interaction**: Support for text, code, images, and file-based conversations
- **Tool Integration**: Seamless integration with development tools, LSP servers, and shell commands
- **Session Management**: Persistent conversation history with file tracking and version control
- **Rich CLI Experience**: Beautiful terminal interface with interactive features

## 🚀 Current Status - Phase 1 Complete ✅

The foundational architecture has been successfully implemented:

- ✅ **Clean Architecture**: Modular design with clear separation of concerns
- ✅ **Domain Models**: Session, Message, and File management with rich semantics
- ✅ **Data Layer**: Optimized Entity Framework Core with SQLite persistence
- ✅ **Service Layer**: Comprehensive business logic with async/await patterns
- ✅ **Console UI**: Beautiful Spectre.Console interface with ASCII art and rich formatting
- ✅ **Dependency Injection**: Modern .NET hosting model with proper DI container
- ✅ **Logging**: Structured logging with Serilog for debugging and monitoring
- ✅ **Testing**: Comprehensive unit test coverage with xUnit and FluentAssertions

## 🏗️ Architecture Overview

## 🤝 Contributing

CodePunk.NET is an open-source project and we welcome contributions! Here's how you can help:

### Development Setup
1. Fork the repository
2. Create a feature branch: `git checkout -b feature/amazing-feature`
3. Make your changes and add tests
4. Ensure all tests pass: `dotnet test`
5. Commit your changes: `git commit -m 'Add amazing feature'`
6. Push to the branch: `git push origin feature/amazing-feature`
7. Open a Pull Request

### Areas for Contribution
- **LLM Provider Integration**: Add support for new AI providers
- **Tool Development**: Create new tools for developer workflows
- **UI/UX Improvements**: Enhance the console interface
- **Testing**: Improve test coverage and add integration tests
- **Documentation**: Help improve docs and examples
- **Performance**: Optimize critical paths and reduce memory usage

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

Built with love for the developer community using:
- The amazing .NET ecosystem and tooling
- Spectre.Console for beautiful terminal experiences
- Entity Framework Core for data persistence
- The open-source community for inspiration and feedback

---

**CodePunk.NET** - Empowering developers with intelligent AI assistance. 🚀

## 🚦 Getting Started

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- SQLite (included with .NET)

### Quick Start

1. **Clone and build**:
   ```bash
   git clone <your-repo-url>
   cd CodePunk.NET
   dotnet build
   ```

2. **Run tests**:
   ```bash
   dotnet test
   ```

3. **Launch the application**:
   ```bash
   cd src/CodePunk.Console
   dotnet run
   ```

You'll see the CodePunk.NET welcome screen with ASCII art and a demonstration of the session management system.

## 💡 Core Features

### 🗂️ Session Management
- **Persistent Conversations**: All AI interactions are saved as sessions with full history
- **Context Preservation**: Maintain conversation context across multiple interactions
- **File Tracking**: Associate files and their versions with specific conversation sessions
- **Metadata**: Rich session metadata including timestamps, titles, and message counts

### 💬 Multi-Modal Messages  
- **Text Content**: Rich text with markdown support and syntax highlighting
- **Tool Integration**: Execute and track tool usage (shell commands, file operations)
- **Image Support**: Handle images in conversations (planned for Phase 2)
- **Structured Data**: Support for complex data types and API responses

### 🔧 Development Integration
- **LSP Integration**: Language Server Protocol support for intelligent code understanding (Phase 2)
- **Shell Commands**: Execute and track shell commands with full output capture
- **File Operations**: Read, write, and modify files with version tracking
- **Project Awareness**: Understanding of project structure, dependencies, and build systems

### 🎨 Rich User Experience
- **Beautiful CLI**: Spectre.Console-powered interface with colors, tables, and progress indicators
- **Interactive Commands**: Rich command-line interface with intelligent tab completion
- **Real-time Updates**: Live status updates and streaming responses
- **Customizable**: Configurable UI themes, logging levels, and behavior preferences

## 🛠️ Technology Stack

- **[.NET 9.0](https://dotnet.microsoft.com/download/dotnet/9.0)**: Latest framework with native AOT and performance improvements
- **[Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/)**: Modern ORM with SQLite provider for local data persistence
- **[Spectre.Console](https://spectreconsole.net/)**: Rich console applications with colors, tables, and interactive widgets
- **[Serilog](https://serilog.net/)**: Structured logging with multiple output targets
- **[Microsoft.Extensions.Hosting](https://docs.microsoft.com/en-us/dotnet/core/extensions/generic-host)**: Generic host for dependency injection and configuration
- **[xUnit](https://xunit.net/) + [FluentAssertions](https://fluentassertions.com/)**: Modern testing framework with readable assertions

## 🗺️ Roadmap

### 🎯 Phase 2: AI Integration (In Progress)
- **LLM Provider Support**: OpenAI GPT-4, Anthropic Claude, and local models
- **Streaming Responses**: Real-time AI response streaming with cancellation support
- **Context Management**: Intelligent context window management and summarization
- **Tool Execution**: Safe execution of AI-generated commands with user approval

### 🎯 Phase 3: Advanced Features
- **Semantic Kernel Integration**: Microsoft's AI orchestration framework
- **Plugin System**: Extensible plugin architecture for custom tools
- **Multi-Agent Conversations**: Support for multiple AI assistants in conversations
- **Advanced UI**: Interactive file browsing, syntax highlighting, and diff views

### 🎯 Phase 4: Enterprise Features  
- **Team Collaboration**: Shared sessions and collaborative coding
- **Integration APIs**: REST APIs for integration with IDEs and other tools
- **Security & Compliance**: Enterprise-grade security and audit logging
- **Performance Optimization**: Advanced caching, indexing, and query optimization

## 🏆 Design Principles

### Clean Architecture
- **Domain-Driven Design**: Rich domain models with business logic encapsulation
- **Separation of Concerns**: Clear boundaries between presentation, business logic, and data
- **Dependency Inversion**: Dependencies point inward toward the domain core
- **Testability**: All components designed for easy unit and integration testing

### Performance First
- **Async/Await**: Non-blocking operations throughout the application stack
- **Compiled Queries**: Optimized Entity Framework queries for frequently used operations
- **Memory Efficiency**: Minimal allocations and efficient data structures
- **Connection Pooling**: Optimized database connection management

### Developer Experience
- **Rich Error Messages**: Detailed error information with suggestions for resolution
- **Comprehensive Logging**: Structured logs for debugging and monitoring
- **IntelliSense Support**: Strong typing and XML documentation for excellent IDE experience
- **Hot Reload**: Fast development cycle with minimal restart requirements

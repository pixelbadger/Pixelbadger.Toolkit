# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This project is a CLI toolkit exposing varied functionality organized by topic.
CLI arguments follow a topic-action pattern: `[topic] [action] [options]`.
For example, the CLI arguments "strings reverse --in-file hw.txt --out-file hw-reversed.txt" would read the content of in-file and output the reversed string to the path of out-file.

The project is .NET 9, and uses Microsoft's System.CommandLine library for building argument sets.

## Development Workflow

### Feature Branch Process

**IMPORTANT**: All new features must be developed in feature branches:
- Create a new branch for each feature: `git checkout -b feature/feature-name`
- Feature branch names must be prefixed with `feature/`
- If you're in the middle of another feature, NEVER start a new one
- Complete the current feature branch before starting any new work
- Merge feature branches back to master when complete

### Development Commands

- **Build**: `dotnet build`
- **Package**: `dotnet pack`
- **Publish to NuGet**: `dotnet nuget push bin/Release/Pixelbadger.Toolkit.*.nupkg --source https://api.nuget.org/v3/index.json --api-key $NUGET_API_KEY`
- **Install as global tool**: `dotnet tool install --global --add-source ./bin/Release Pixelbadger.Toolkit`
- **Run (from source)**: `dotnet run -- [topic] [action] [options]`
- **Run (global tool)**: `pbtk [topic] [action] [options]`
- **Run with examples**:
  - `pbtk strings reverse --in-file hello.txt --out-file hello-reversed.txt`
  - `pbtk strings levenshtein-distance --string1 "hello" --string2 "world"`
  - `pbtk search ingest --index-path ./index --content-path document.txt`
  - `pbtk search query --index-path ./index --query "hello world"`
  - `pbtk openai chat --message "Hello, how are you?"`
  - `pbtk openai chat --message "Continue our conversation" --chat-history ./chat.json --model "gpt-4o-mini"`
  - `pbtk openai translate --text "Hello, how are you?" --target-language "Spanish"`
  - `pbtk openai ocaaar --image-path ./image.jpg`

## Architecture

The project follows a topic-based command architecture with a component-per-command pattern:

- **Program.cs**: Entry point that registers all topic commands with the root command
- **Commands/**: Contains topic command definitions, each topic has a static `Create()` method that registers sub-actions
- **Components/**: Contains the core business logic implementations that commands delegate to

## Component-Per-Command Pattern

**IMPORTANT**: Each command action should have its own dedicated component class. This enforces single responsibility principle and improves maintainability.

### Pattern Guidelines:
1. **One Component Per Command**: Each command action (e.g., `chat`, `translate`, `ocaaar`) should have its own component class
2. **Composition Over Inheritance**: Use dependency injection with service classes for shared functionality rather than base classes
3. **Clear Naming**: Component names should match the command action (e.g., `ChatComponent`, `TranslateComponent`, `OcaaarComponent`)
4. **Single Responsibility**: Each component should handle only one specific functionality
5. **Minimal Dependencies**: Components should only depend on what they actually need
6. **Interface-Based Services**: Create interfaces for shared services to enable easier testing and flexibility

### Example Structure:
```
Services/
└── OpenAiClientService.cs      # Shared OpenAI functionality (with interface)

Components/
├── ChatComponent.cs            # Chat command logic (injects IOpenAiClientService)
├── TranslateComponent.cs       # Translate command logic (injects IOpenAiClientService)
└── OcaaarComponent.cs          # Ocaaar command logic (injects IOpenAiClientService)
```

### Dependency Injection Pattern:
- Components receive dependencies through constructor injection
- Shared functionality is provided through service classes with interfaces
- Commands instantiate services and inject them into components
- This approach makes components more testable and loosely coupled

Each topic command follows the pattern:
1. Create a main topic command with description
2. Add sub-commands (actions) for each functionality within that topic
3. Each action defines options/arguments with System.CommandLine
4. Set up handlers that delegate to dedicated component classes (one per action)
5. Handle errors and provide user feedback

Topic commands are registered in Program.cs by calling their static `Create()` methods and adding them to the root command.

Available topics and actions:
- **strings**: reverse, levenshtein-distance
- **search**: ingest, query
- **interpreters**: brainfuck, ook
- **images**: steganography
- **web**: serve-html
- **openai**: chat, translate, ocaaar

## Dependencies

- Microsoft.AspNetCore.App (framework reference)
- Microsoft.Extensions.Hosting
- Lucene.Net (for search indexing)
- SixLabors.ImageSharp (for steganography)
- System.CommandLine (beta)
- OpenAI (for LLM integration)
- Microsoft.Extensions.AI (for AI abstractions)
- Microsoft.Extensions.AI.OpenAI (for OpenAI integration)

## Environment Variables

- **OPENAI_API_KEY**: Required for LLM functionality (openai, translate, ocaaar actions)
- **NUGET_API_KEY**: Required for publishing packages to NuGet

## Important Instructions

Do what has been asked; nothing more, nothing less.
NEVER create files unless they're absolutely necessary for achieving your goal.
ALWAYS prefer editing an existing file to creating a new one.
NEVER proactively create documentation files (*.md) or README files. Only create documentation files if explicitly requested by the User.
ALWAYS create test assets (test files, sample data, etc.) in the test-assets/ folder which is gitignored.
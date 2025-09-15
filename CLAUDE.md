# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This project is a CLI toolkit exposing varied functionality organized by topic.
CLI arguments follow a topic-action pattern: `[topic] [action] [options]`.
For example, the CLI arguments "strings reverse --in-file hw.txt --out-file hw-reversed.txt" would read the content of in-file and output the reversed string to the path of out-file.

The project is .NET 9, and uses Microsoft's System.CommandLine library for building argument sets.

## Development Commands

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
  - `pbtk llm openai --message "Hello, how are you?"`
  - `pbtk llm openai --message "Continue our conversation" --chat-history ./chat.json --model "gpt-4o-mini"`
  - `pbtk llm translate --text "Hello, how are you?" --target-language "Spanish"`
  - `pbtk llm ocaaar --image-path ./image.jpg`

## Architecture

The project follows a topic-based command architecture:

- **Program.cs**: Entry point that registers all topic commands with the root command
- **Commands/**: Contains topic command definitions, each topic has a static `Create()` method that registers sub-actions
- **Components/**: Contains the core business logic implementations that commands delegate to

Each topic command follows the pattern:
1. Create a main topic command with description
2. Add sub-commands (actions) for each functionality within that topic
3. Each action defines options/arguments with System.CommandLine
4. Set up handlers that delegate to component classes
5. Handle errors and provide user feedback

Topic commands are registered in Program.cs by calling their static `Create()` methods and adding them to the root command.

Available topics and actions:
- **strings**: reverse, levenshtein-distance
- **search**: ingest, query
- **interpreters**: brainfuck, ook
- **images**: steganography
- **web**: serve-html
- **llm**: openai, translate, ocaaar

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
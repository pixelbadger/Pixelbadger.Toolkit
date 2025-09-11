# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This project is a CLI toolkit exposing varied functionality organized by topic.
CLI arguments follow a topic-action pattern: `[topic] [action] [options]`.
For example, the CLI arguments "strings reverse --in-file hw.txt --out-file hw-reversed.txt" would read the content of in-file and output the reversed string to the path of out-file.

The project is .NET 8, and uses Microsoft's System.CommandLine library for building argument sets.

## Development Commands

- **Build**: `dotnet build`
- **Run**: `dotnet run -- [topic] [action] [options]`
- **Run with examples**: 
  - `dotnet run -- strings reverse --in-file hello.txt --out-file hello-reversed.txt`
  - `dotnet run -- algorithms levenshtein-distance --string1 "hello" --string2 "world"`

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
- **strings**: reverse
- **algorithms**: levenshtein-distance
- **interpreters**: brainfuck, ook
- **images**: steganography
- **web**: serve-html

## Dependencies

- Microsoft.AspNetCore.App (framework reference)
- Microsoft.Extensions.Hosting
- SixLabors.ImageSharp (for steganography)
- System.CommandLine (beta)

## Important Instructions

Do what has been asked; nothing more, nothing less.
NEVER create files unless they're absolutely necessary for achieving your goal.
ALWAYS prefer editing an existing file to creating a new one.
NEVER proactively create documentation files (*.md) or README files. Only create documentation files if explicitly requested by the User.
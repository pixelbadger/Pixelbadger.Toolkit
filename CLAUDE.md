# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This project is a CLI toolkit exposing varied functionality.
CLI arguments start with an action, then the required arguments for that action.
For example, the CLI arguments "reverse-string --in-file hw.txt --out-file hw-reversed.txt" would read the content of in-file and output the reversed string to the path of out-file.

The project is .NET 8, and uses Microsoft's System.CommandLine library for building argument sets.

## Development Commands

- **Build**: `dotnet build`
- **Run**: `dotnet run -- [command] [options]`
- **Run with examples**: 
  - `dotnet run -- reverse-string --in-file hello.txt --out-file hello-reversed.txt`
  - `dotnet run -- levenshtein-distance --string1 "hello" --string2 "world"`

## Architecture

The project follows a modular command-based architecture:

- **Program.cs**: Entry point that registers all available commands with the root command
- **Commands/**: Contains command definitions using System.CommandLine, each command has a static `Create()` method
- **Components/**: Contains the core business logic implementations that commands delegate to

Each command follows the pattern:
1. Define options/arguments with System.CommandLine
2. Set up a handler that delegates to a component class
3. Handle errors and provide user feedback

Commands are registered in Program.cs by calling their static `Create()` methods and adding them to the root command.

Available commands: reverse-string, levenshtein-distance, brainfuck, ook, steganography, serve-html

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
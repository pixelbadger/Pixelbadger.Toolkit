# Pixelbadger.Toolkit

A CLI toolkit exposing varied functionality organized by topic. The CLI uses [Spectre.Console](https://spectreconsole.net/) for rich terminal output — colour-coded success and error messages, formatted tables for data-heavy commands, and styled interactive prompts for secure input.

> **Note**: Search and MCP RAG functionality has been extracted to the separate [Pixelbadger.Toolkit.Rag](https://github.com/pixelbadger/Pixelbadger.Toolkit.Rag) repository (`pbrag` CLI tool).

## Installation

```bash
dotnet tool install --global Pixelbadger.Toolkit
```

## Usage

```
pbtk [topic] [action] [options]
```

Get help for any command:
```bash
pbtk --help
pbtk strings reverse --help
```

Full command documentation is available at the [project docs site](https://pixelbadger.github.io/Pixelbadger.Toolkit/).

## Development

### Build

```bash
dotnet build
dotnet test
```

### Package and publish

```bash
dotnet pack
dotnet nuget push bin/Release/Pixelbadger.Toolkit.*.nupkg --source https://api.nuget.org/v3/index.json --api-key $NUGET_API_KEY
```

### Run from source

```bash
dotnet run -- [topic] [action] [options]
```

### Install locally

```bash
dotnet tool install --global --add-source ./bin/Release Pixelbadger.Toolkit
```

## Topics

| Topic | Description |
|---|---|
| `strings` | String manipulation — reverse, Levenshtein distance, abjadify, Flesch reading ease, report |
| `interpreters` | Esoteric language interpreters — Brainfuck, Ook, bf-to-ook |
| `images` | Image steganography — hide and recover messages in PNG files |
| `web` | Local web server — serve a single HTML file over HTTP |
| `llm` | LLM utilities — chat, translate, OCR, corpospeak, session history |
| `oauth` | OAuth token management — acquire tokens, manage connection profiles |
| `crypto` | Paillier homomorphic encryption — encrypt, decrypt, and perform arithmetic on ciphertext |
| `markov` | Markov chain text generation — train a model from a corpus, generate completions |
| `demoscene` | Classic Amiga demoscene effects — Kefrens bars and Boing Ball rendered in the terminal |

## Requirements

- .NET 9.0
- OpenAI API key (for `llm` commands) — set the `OPENAI_API_KEY` environment variable

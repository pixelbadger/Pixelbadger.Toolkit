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

### gpt

A tiny char-level GPT (decoder-only transformer) implemented entirely from scratch in pure C# — a hand-rolled tensor library with reverse-mode autograd, manual forward and backward passes, and an AdamW optimizer. Inspired by Karpathy's nanoGPT/microGPT. Hot kernels are accelerated with SIMD via `System.Numerics.Tensors` (`TensorPrimitives`) and `Parallel.For` across independent matmul rows; reductions are kept sequential so results are reproducible regardless of thread count.

A checkpoint is a directory containing `config.json` (model config + vocabulary) and `weights.bin` (raw parameter floats).

#### train

Builds a character vocabulary from the corpus, trains the model by gradient descent, and writes a checkpoint.

```
--source <path>       Path to a training corpus file, or literal text (required)
--out <dir>           Checkpoint output directory (default: ./.gpt)
--steps <int>         Number of training steps (default: 2000)
--batch-size <int>    Sequences per training step (default: 16)
--block-size <int>    Context length in characters (default: 64)
--n-embd <int>        Embedding dimension (default: 128)
--n-head <int>        Number of attention heads (default: 4)
--n-layer <int>       Number of transformer blocks (default: 3)
--lr <float>          Learning rate (default: 0.0003)
--seed <int>          Random seed for reproducible runs (default: 1337)
```
```bash
pbtk gpt train --source corpus.txt --out ./.gpt --steps 2000
```

> `--n-embd` must be divisible by `--n-head`, and the corpus must be longer than `--block-size`.

#### complete

Loads a checkpoint and autoregressively generates a continuation of the prompt.

```
--model <dir>          Checkpoint directory (default: ./.gpt)
--prompt <text>        Prompt to continue (required)
--max-tokens <int>     Number of characters to generate (default: 200)
--temperature <float>  Sampling temperature; 0 = greedy/deterministic (default: 0.8)
--seed <int>           Random seed for sampling (default: 1337)
```
```bash
pbtk gpt complete --model ./.gpt --prompt "ROMEO:" --max-tokens 200
pbtk gpt complete --model ./.gpt --prompt "The" --temperature 0
```

---

## Requirements

- .NET 9.0
- OpenAI API key (for `llm` commands) — set the `OPENAI_API_KEY` environment variable

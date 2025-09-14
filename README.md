# Pixelbadger.Toolkit

A CLI toolkit exposing varied functionality organized by topic, including string manipulation, distance calculations, esoteric programming language interpreters, image steganography, web serving, LLM integration, and Model Context Protocol (MCP) servers.

## Table of Contents

- [Installation](#installation)
- [Usage](#usage)
- [Available Topics and Actions](#available-topics-and-actions)
  - [strings](#strings)
    - [reverse](#reverse)
    - [levenshtein-distance](#levenshtein-distance)
  - [search](#search)
    - [ingest](#ingest)
    - [query](#query)
  - [interpreters](#interpreters)
    - [brainfuck](#brainfuck)
    - [ook](#ook)
  - [images](#images)
    - [steganography](#steganography)
  - [web](#web)
    - [serve-html](#serve-html)
  - [llm](#llm)
    - [openai](#openai)
  - [mcp](#mcp)
    - [rag-server](#rag-server)
- [Help](#help)
- [Requirements](#requirements)
- [Technical Details](#technical-details)
  - [Steganography Implementation](#steganography-implementation)
  - [Supported Languages](#supported-languages)

## Installation

Build the project with:
```bash
dotnet build
```

## Usage

Run commands using the topic-action pattern:
```bash
dotnet run -- [topic] [action] [options]
```

## Available Topics and Actions

### strings
String manipulation utilities.

#### reverse
Reverses the content of a file.

**Usage:**
```bash
dotnet run -- strings reverse --in-file <input-file> --out-file <output-file>
```

**Example:**
```bash
dotnet run -- strings reverse --in-file hello.txt --out-file hello-reversed.txt
```

#### levenshtein-distance
Calculates the Levenshtein distance between two strings or files.

**Usage:**
```bash
dotnet run -- strings levenshtein-distance --string1 <string1> --string2 <string2>
```

**Examples:**
```bash
# Compare two strings directly
dotnet run -- strings levenshtein-distance --string1 "hello" --string2 "world"

# Compare contents of two files
dotnet run -- strings levenshtein-distance --string1 file1.txt --string2 file2.txt
```

### search
Search indexing and querying utilities.

#### ingest
Ingest content into a Lucene.NET search index with intelligent chunking based on file type.

**Usage:**
```bash
dotnet run -- search ingest --index-path <index-directory> --content-path <content-file>
```

**Examples:**
```bash
# Ingest a text file into a search index (paragraph chunking)
dotnet run -- search ingest --index-path ./search-index --content-path document.txt

# Ingest markdown content (header-based chunking)
dotnet run -- search ingest --index-path ./docs-index --content-path README.md
```

**Details:**
- **Markdown files** (.md, .markdown): Automatically chunked by headers (# ## ###) for semantic organization
- **Other files**: Split into paragraphs (separated by double newlines, or single newlines if no double newlines found)
- Each chunk becomes a separate searchable document in the index
- Metadata is stored including source file, chunk/paragraph number, and document ID
- Creates a new index if it doesn't exist, or adds to an existing index
- Markdown chunks preserve header context and hierarchy information

#### query
Perform BM25 similarity search against a Lucene.NET index with optional source ID filtering.

**Usage:**
```bash
dotnet run -- search query --index-path <index-directory> --query <search-terms> [--max-results <number>] [--sourceIds <id1> <id2> ...]
```

**Examples:**
```bash
# Search for documents containing specific terms
dotnet run -- search query --index-path ./search-index --query "hello world"

# Limit results to 5 documents
dotnet run -- search query --index-path ./docs-index --query "lucene search" --max-results 5

# Complex query with operators
dotnet run -- search query --index-path ./index --query "\"exact phrase\" OR keyword"

# Filter results by source IDs (based on filename without extension)
dotnet run -- search query --index-path ./index --query "search terms" --sourceIds document1 readme
```

**Details:**
- Uses BM25 similarity ranking for relevance scoring
- Returns results sorted by relevance score (highest first)
- Supports Lucene query syntax including phrases, boolean operators, wildcards
- Shows source file, paragraph number, relevance score, and content for each result
- Default maximum results is 10
- Optional source ID filtering constrains results to documents from specific files
- Source IDs are derived from filenames (without extension) during ingestion

### interpreters
Esoteric programming language interpreters.

#### brainfuck
Executes a Brainfuck program from a file.

**Usage:**
```bash
dotnet run -- interpreters brainfuck --file <program-file>
```

**Example:**
```bash
dotnet run -- interpreters brainfuck --file hello-world.bf
```

#### ook
Executes an Ook program from a file.

**Usage:**
```bash
dotnet run -- interpreters ook --file <program-file>
```

**Example:**
```bash
dotnet run -- interpreters ook --file hello-world.ook
```

### images
Image processing and manipulation utilities.

#### steganography
Encode or decode hidden messages in images using least significant bit (LSB) steganography.

**Usage:**

**Encoding a message:**
```bash
dotnet run -- images steganography --mode encode --image <input-image> --message <message> --output <output-image>
```

**Decoding a message:**
```bash
dotnet run -- images steganography --mode decode --image <encoded-image>
```

**Examples:**
```bash
# Hide a secret message in an image
dotnet run -- images steganography --mode encode --image photo.jpg --message "This is secret!" --output encoded.png

# Extract the hidden message
dotnet run -- images steganography --mode decode --image encoded.png
```

### web
Web server utilities.

#### serve-html
Serves a static HTML file via HTTP server.

**Usage:**
```bash
dotnet run -- web serve-html --file <html-file> [--port <port>]
```

**Options:**
- `--file`: Path to the HTML file to serve (required)
- `--port`: Port to bind the server to (default: 8080)

**Examples:**
```bash
# Serve an HTML file on default port 8080
dotnet run -- web serve-html --file index.html

# Serve on a specific port
dotnet run -- web serve-html --file test.html --port 3000
```

### llm
Large Language Model utilities.

#### openai
Chat with OpenAI models maintaining conversation history.

**Usage:**
```bash
dotnet run -- llm openai --message <message> [--chat-history <history-file>] [--model <model-name>]
```

**Options:**
- `--message`: The message to send to the LLM (required)
- `--chat-history`: Path to JSON file containing chat history (optional, will be created if it doesn't exist)
- `--model`: The OpenAI model to use (optional, default: gpt-5-nano)

**Examples:**
```bash
# Simple message without history
dotnet run -- llm openai --message "What is the capital of France?"

# Start a conversation with history tracking
dotnet run -- llm openai --message "Hello, my name is Alice" --chat-history ./chat.json

# Continue the conversation (remembers previous context)
dotnet run -- llm openai --message "What's my name?" --chat-history ./chat.json

# Use a specific model
dotnet run -- llm openai --message "Explain quantum computing" --model "gpt-4o-mini"

# Complex conversation with specific model and history
dotnet run -- llm openai --message "Continue our discussion about AI" --chat-history ./ai-chat.json --model "gpt-4o"
```

**Details:**
- Requires `OPENAI_API_KEY` environment variable to be set
- Chat history is stored in JSON format with role/content pairs
- Maintains full conversation context across multiple interactions
- Supports all OpenAI chat models (gpt-3.5-turbo, gpt-4, gpt-4o, gpt-5-nano, etc.)
- Each conversation turn includes both user message and assistant response
- History files are created automatically if they don't exist
- Compatible with OpenAI API key authentication

**Environment Setup:**
```bash
# Set your OpenAI API key
export OPENAI_API_KEY="your-api-key-here"

# Then use the llm commands
dotnet run -- llm openai --message "Hello!"
```

### mcp
Model Context Protocol server utilities for AI integration.

#### rag-server
Hosts an MCP server that performs BM25 similarity search against a Lucene.NET index, enabling AI assistants to retrieve relevant context from your documents.

**Usage:**
```bash
dotnet run -- mcp rag-server --index-path <index-directory>
```

**Options:**
- `--index-path`: Path to the Lucene.NET index directory (required)

**Examples:**
```bash
# Start MCP server with an existing search index
dotnet run -- mcp rag-server --index-path ./search-index

# Use with Claude Desktop or other MCP clients
dotnet run -- mcp rag-server --index-path ./docs-index
```

**Details:**
- Communicates via stdin/stdout using JSON-RPC protocol
- Provides the `search` MCP tool that performs BM25 queries against the index with configurable result limits and source ID filtering
- Returns formatted search results with relevance scores, source files, paragraph numbers, source IDs, and content
- Supports optional source ID filtering to constrain results to specific documents
- Compatible with MCP clients like Claude Desktop, Continue, and other AI development tools
- Requires an existing Lucene index created with the `search ingest` command

**MCP Tool Parameters:**
- `query` (required): The search query text
- `maxResults` (optional, default: 5): Maximum number of results to return
- `sourceIds` (optional): Array of source IDs to filter results to specific documents

**Example MCP Tool Usage:**
```json
{
  "name": "search",
  "arguments": {
    "query": "programming concepts",
    "maxResults": 3,
    "sourceIds": ["document1", "readme"]
  }
}
```

**Integration Example:**
First create an index, then start the MCP server:
```bash
# 1. Create search index from your documents
dotnet run -- search ingest --index-path ./my-docs --content-path documentation.md

# 2. Start MCP server for AI integration
dotnet run -- mcp rag-server --index-path ./my-docs
```

## Help

Get help for any command by adding `--help`:
```bash
dotnet run -- --help                    # General help
dotnet run -- reverse-string --help     # Command-specific help
```

## Requirements

- .NET 9.0
- SixLabors.ImageSharp (for steganography features)
- OpenAI API key (for LLM features)

## Technical Details

### Steganography Implementation
The steganography feature uses LSB (Least Significant Bit) encoding to hide messages in the RGB color channels of images. Each bit of the message is stored in the least significant bit of the red, green, or blue color values, making the changes imperceptible to the human eye.

### Supported Languages
- **Brainfuck**: A minimalist esoteric programming language with 8 commands
- **Ook**: A Brainfuck derivative using "Ook." and "Ook?" syntax inspired by Terry Pratchett's Discworld orangutans
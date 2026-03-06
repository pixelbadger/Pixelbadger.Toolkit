# Pixelbadger.Toolkit

A CLI toolkit exposing varied functionality organized by topic, including string manipulation, distance calculations, esoteric programming language interpreters, image steganography, web serving, and OpenAI integration.

> **Note**: Search and MCP RAG functionality has been extracted to the separate [Pixelbadger.Toolkit.Rag](https://github.com/pixelbadger/Pixelbadger.Toolkit.Rag) repository (`pbrag` CLI tool).

## Table of Contents

- [Installation](#installation)
- [Usage](#usage)
- [Available Topics and Actions](#available-topics-and-actions)
  - [strings](#strings)
    - [reverse](#reverse)
    - [levenshtein-distance](#levenshtein-distance)
    - [flesch-reading-ease](#flesch-reading-ease)
  - [interpreters](#interpreters)
    - [brainfuck](#brainfuck)
    - [ook](#ook)
    - [bf-to-ook](#bf-to-ook)
  - [images](#images)
    - [steganography](#steganography)
  - [web](#web)
    - [serve-html](#serve-html)
  - [openai](#openai)
    - [chat](#chat)
    - [translate](#translate)
    - [ocaaar](#ocaaar)
    - [corpospeak](#corpospeak)
- [Help](#help)
- [Requirements](#requirements)
- [Technical Details](#technical-details)
  - [Steganography Implementation](#steganography-implementation)
  - [Supported Languages](#supported-languages)

## Installation

### Option 1: Install as .NET Global Tool (Recommended)

Install the tool globally using the NuGet package:
```bash
dotnet tool install --global Pixelbadger.Toolkit
```

Once installed, you can use the `pbtk` command from anywhere:
```bash
pbtk --help
```

### Option 2: Build from Source

Clone the repository and build the project:
```bash
git clone https://github.com/pixelbadger/Pixelbadger.Toolkit.git
cd Pixelbadger.Toolkit
dotnet build
```

## Usage

### Using the Global Tool (pbtk)
Run commands using the topic-action pattern:
```bash
pbtk [topic] [action] [options]
```

### Using from Source
If building from source, use:
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
# Using global tool
pbtk strings reverse --in-file <input-file> --out-file <output-file>

# Using from source
dotnet run -- strings reverse --in-file <input-file> --out-file <output-file>
```

**Example:**
```bash
pbtk strings reverse --in-file hello.txt --out-file hello-reversed.txt
```

#### levenshtein-distance
Calculates the Levenshtein distance between two strings or files.

**Usage:**
```bash
pbtk strings levenshtein-distance --string1 <string1> --string2 <string2>
```

**Examples:**
```bash
# Compare two strings directly
pbtk strings levenshtein-distance --string1 "hello" --string2 "world"

# Compare contents of two files
pbtk strings levenshtein-distance --string1 file1.txt --string2 file2.txt
```

#### flesch-reading-ease
Analyzes a plain-text file and reports its Flesch Reading Ease score.

**Usage:**
```bash
pbtk strings flesch-reading-ease --in-file <input-file>
```

**Options:**
- `--in-file`: Path to the plain-text input file (required)

**Example:**
```bash
pbtk strings flesch-reading-ease --in-file article.txt
```

**Output includes:**
- Flesch Reading Ease score
- Readability band (Very easy, Easy, Standard, etc.)
- Sentence count
- Word count
- Syllable count

### interpreters
Esoteric programming language interpreters.

#### brainfuck
Executes a Brainfuck program from a file.

**Usage:**
```bash
pbtk interpreters brainfuck --file <program-file>
```

**Example:**
```bash
pbtk interpreters brainfuck --file hello-world.bf
```

#### ook
Executes an Ook program from a file.

**Usage:**
```bash
pbtk interpreters ook --file <program-file>
```

**Example:**
```bash
pbtk interpreters ook --file hello-world.ook
```

#### bf-to-ook
Converts a Brainfuck program to Ook language.

**Usage:**
```bash
pbtk interpreters bf-to-ook --source <brainfuck-file> --output <ook-file>
```

**Options:**
- `--source`: Path to the source Brainfuck program file (required)
- `--output`: Path to the output Ook program file (required)

**Examples:**
```bash
# Convert a Brainfuck program to Ook
pbtk interpreters bf-to-ook --source hello.bf --output hello.ook

# Convert and then execute the Ook program
pbtk interpreters bf-to-ook --source program.bf --output program.ook
pbtk interpreters ook --file program.ook
```

**Details:**
- Translates Brainfuck commands to their Ook equivalents:
  - `>` → `Ook. Ook?`
  - `<` → `Ook? Ook.`
  - `+` → `Ook. Ook.`
  - `-` → `Ook! Ook!`
  - `.` → `Ook! Ook.`
  - `,` → `Ook. Ook!`
  - `[` → `Ook! Ook?`
  - `]` → `Ook? Ook!`
- Creates output directory if it doesn't exist
- Generated Ook programs are functionally equivalent to the original Brainfuck programs
- Perfect for converting existing Brainfuck code to Ook for educational or entertainment purposes

### images
Image processing and manipulation utilities.

#### steganography
Encode or decode hidden messages in images using least significant bit (LSB) steganography.

**Usage:**

**Encoding a message:**
```bash
pbtk images steganography --mode encode --image <input-image> --message <message> --output <output-image>
```

**Decoding a message:**
```bash
pbtk images steganography --mode decode --image <encoded-image>
```

**Examples:**
```bash
# Hide a secret message in an image
pbtk images steganography --mode encode --image photo.jpg --message "This is secret!" --output encoded.png

# Extract the hidden message
pbtk images steganography --mode decode --image encoded.png
```

### web
Web server utilities.

#### serve-html
Serves a static HTML file via HTTP server.

**Usage:**
```bash
pbtk web serve-html --file <html-file> [--port <port>]
```

**Options:**
- `--file`: Path to the HTML file to serve (required)
- `--port`: Port to bind the server to (default: 8080)

**Examples:**
```bash
# Serve an HTML file on default port 8080
pbtk web serve-html --file index.html

# Serve on a specific port
pbtk web serve-html --file test.html --port 3000
```

### openai
OpenAI utilities.

#### chat
Chat with OpenAI models maintaining conversation history.

**Usage:**
```bash
pbtk openai chat --message <message> [--chat-history <history-file>] [--model <model-name>]
```

**Options:**
- `--message`: The message to send to the LLM (required)
- `--chat-history`: Path to JSON file containing chat history (optional, will be created if it doesn't exist)
- `--model`: The OpenAI model to use (optional, default: gpt-5-nano)

**Examples:**
```bash
# Simple message without history
pbtk openai chat --message "What is the capital of France?"

# Start a conversation with history tracking
pbtk openai chat --message "Hello, my name is Alice" --chat-history ./chat.json

# Continue the conversation (remembers previous context)
pbtk openai chat --message "What's my name?" --chat-history ./chat.json

# Use a specific model
pbtk openai chat --message "Explain quantum computing" --model "gpt-4o-mini"

# Complex conversation with specific model and history
pbtk openai chat --message "Continue our discussion about AI" --chat-history ./ai-chat.json --model "gpt-4o"
```

**Details:**
- Requires `OPENAI_API_KEY` environment variable to be set
- Chat history is stored in JSON format with role/content pairs
- Maintains full conversation context across multiple interactions
- Supports all OpenAI chat models (gpt-3.5-turbo, gpt-4, gpt-4o, gpt-5-nano, etc.)
- Each conversation turn includes both user message and assistant response
- History files are created automatically if they don't exist
- Compatible with OpenAI API key authentication

#### translate
Translate text to a target language using OpenAI.

**Usage:**
```bash
pbtk openai translate --text <text-to-translate> --target-language <target-language> [--model <model-name>]
```

**Options:**
- `--text`: The text to translate (required)
- `--target-language`: The target language to translate to (required)
- `--model`: The OpenAI model to use (optional, default: gpt-5-nano)

**Examples:**
```bash
# Translate text to Spanish
pbtk openai translate --text "Hello, how are you?" --target-language "Spanish"

# Translate to French using a specific model
pbtk openai translate --text "Good morning" --target-language "French" --model "gpt-4o-mini"

# Translate complex text
pbtk openai translate --text "The weather is beautiful today" --target-language "German"
```

**Environment Setup:**
```bash
# Set your OpenAI API key
export OPENAI_API_KEY="your-api-key-here"

# Then use the openai commands
pbtk openai chat --message "Hello!"
pbtk openai translate --text "Hello!" --target-language "Spanish"
pbtk openai ocaaar --image-path "./image.jpg"
pbtk openai corpospeak --source "Hello!" --audience "csuite"
pbtk openai corpospeak --source "./content.txt" --audience "engineering" --user-messages "./style.txt"
```

#### ocaaar
Extract text from an image and translate it to pirate speak using OpenAI vision capabilities.

**Usage:**
```bash
pbtk openai ocaaar --image-path <image-file> [--model <model-name>]
```

**Options:**
- `--image-path`: Path to the image file to process (required)
- `--model`: The OpenAI model to use (optional, default: gpt-5-nano)

**Examples:**
```bash
# Extract text from an image and get pirate translation
pbtk openai ocaaar --image-path poster.jpg

# Use a specific model for better OCR accuracy
pbtk openai ocaaar --image-path document.png --model "gpt-4o"

# Process a screenshot with text
pbtk openai ocaaar --image-path screenshot.png
```

**Details:**
- Requires `OPENAI_API_KEY` environment variable to be set
- Supports common image formats: JPEG, PNG, GIF, WebP
- Uses OpenAI's vision capabilities for text extraction
- Automatically translates extracted text to pirate dialect
- Returns only the pirate-translated text without additional commentary
- Perfect for humorous OCR processing of signs, documents, or any text-containing images

#### corpospeak
Rewrite text for enterprise audiences with optional idiolect adaptation using OpenAI.

**Usage:**
```bash
pbtk openai corpospeak --source <source-text-or-file> --audience <target-audience> [--user-messages <message1-or-file> <message2-or-file> ...] [--model <model-name>]
```

**Options:**
- `--source`: The source text to rewrite (or path to file containing the text) (required)
- `--audience`: Target audience - one of: csuite, engineering, product, sales, marketing, operations, finance, legal, hr, customer-success (required)
- `--user-messages`: Optional user messages to learn writing style from (text or file paths, multiple values allowed)
- `--model`: The OpenAI model to use (optional, default: gpt-5-nano)

**Examples:**
```bash
# Basic audience conversion with direct text
pbtk openai corpospeak --source "API performance is great" --audience "csuite"

# Convert for engineering team using file input
pbtk openai corpospeak --source ./technical-update.txt --audience "engineering"

# With idiolect adaptation using direct text examples
pbtk openai corpospeak --source "System upgrade complete" --audience "sales" --user-messages "Hey team!" "Let's crush this quarter!"

# With idiolect adaptation using file-based examples
pbtk openai corpospeak --source ./announcement.txt --audience "product" --user-messages ./user-style1.txt ./user-style2.txt

# Mixed file and text inputs
pbtk openai corpospeak --source ./release-notes.txt --audience "marketing" --user-messages "Our users love this!" ./brand-voice.txt

# Use specific model for better results
pbtk openai corpospeak --source "Database migration finished" --audience "operations" --model "gpt-4o"
```

**Details:**
- Requires `OPENAI_API_KEY` environment variable to be set
- **Two-stage processing**: First converts text for target audience, then optionally adapts to user's writing style
- **Separate chat instances**: Audience conversion and idiolect rewrite use independent OpenAI conversations
- **Comprehensive audience support**: Covers major enterprise tech organization roles
- **Robust validation**: Validates audience parameters with helpful error messages
- Perfect for adapting technical content for different stakeholders while maintaining accuracy

**Supported Audiences:**
- **csuite/executive**: Strategic, business impact focused language
- **engineering**: Technical precision and implementation details
- **product**: User impact and feature strategy focus
- **sales**: Value propositions and competitive advantages
- **marketing**: Market appeal and customer messaging
- **operations**: Scalability and reliability emphasis
- **finance**: Cost implications and ROI focus
- **legal**: Risk assessment and compliance considerations
- **hr**: People impact and organizational dynamics
- **customer-success**: Customer experience and support focus

## Help

Get help for any command by adding `--help`:
```bash
# Using global tool
pbtk --help                              # General help
pbtk strings reverse --help              # Command-specific help

# Using from source
dotnet run -- --help                     # General help
dotnet run -- strings reverse --help     # Command-specific help
```

## Requirements

- .NET 9.0
- SixLabors.ImageSharp (for steganography features)
- OpenAI API key (for OpenAI features)

## Technical Details

### Steganography Implementation
The steganography feature uses LSB (Least Significant Bit) encoding to hide messages in the RGB color channels of images. Each bit of the message is stored in the least significant bit of the red, green, or blue color values, making the changes imperceptible to the human eye.

### Supported Languages
- **Brainfuck**: A minimalist esoteric programming language with 8 commands
- **Ook**: A Brainfuck derivative using "Ook." and "Ook?" syntax inspired by Terry Pratchett's Discworld orangutans

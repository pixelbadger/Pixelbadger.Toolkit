# Pixelbadger.Toolkit

A CLI toolkit exposing varied functionality organized by topic, including string manipulation, distance calculations, esoteric programming language interpreters, steganography, and web serving.

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
Ingest content into a Lucene.NET search index by splitting it into paragraph chunks.

**Usage:**
```bash
dotnet run -- search ingest --index-path <index-directory> --content-path <content-file>
```

**Examples:**
```bash
# Ingest a text file into a search index
dotnet run -- search ingest --index-path ./search-index --content-path document.txt

# Ingest markdown content
dotnet run -- search ingest --index-path ./docs-index --content-path README.md
```

**Details:**
- Content is automatically split into paragraphs (separated by double newlines, or single newlines if no double newlines found)
- Each paragraph becomes a separate searchable document in the index
- Metadata is stored including source file, paragraph number, and document ID
- Creates a new index if it doesn't exist, or adds to an existing index

#### query
Perform BM25 similarity search against a Lucene.NET index.

**Usage:**
```bash
dotnet run -- search query --index-path <index-directory> --query <search-terms> [--max-results <number>]
```

**Examples:**
```bash
# Search for documents containing specific terms
dotnet run -- search query --index-path ./search-index --query "hello world"

# Limit results to 5 documents
dotnet run -- search query --index-path ./docs-index --query "lucene search" --max-results 5

# Complex query with operators
dotnet run -- search query --index-path ./index --query "\"exact phrase\" OR keyword"
```

**Details:**
- Uses BM25 similarity ranking for relevance scoring
- Returns results sorted by relevance score (highest first)
- Supports Lucene query syntax including phrases, boolean operators, wildcards
- Shows source file, paragraph number, relevance score, and content for each result
- Default maximum results is 10

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

## Help

Get help for any command by adding `--help`:
```bash
dotnet run -- --help                    # General help
dotnet run -- reverse-string --help     # Command-specific help
```

## Requirements

- .NET 9.0
- SixLabors.ImageSharp (for steganography features)

## Technical Details

### Steganography Implementation
The steganography feature uses LSB (Least Significant Bit) encoding to hide messages in the RGB color channels of images. Each bit of the message is stored in the least significant bit of the red, green, or blue color values, making the changes imperceptible to the human eye.

### Supported Languages
- **Brainfuck**: A minimalist esoteric programming language with 8 commands
- **Ook**: A Brainfuck derivative using "Ook." and "Ook?" syntax inspired by Terry Pratchett's Discworld orangutans
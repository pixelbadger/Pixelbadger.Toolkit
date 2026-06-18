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

## Topics and Actions

### strings

#### reverse
```
--in-file <path>    Input file path (required)
--out-file <path>   Output file path (required)
```
```bash
pbtk strings reverse --in-file hello.txt --out-file hello-reversed.txt
```

#### levenshtein-distance
```
--string1 <value>   First string or file path (required)
--string2 <value>   Second string or file path (required)
```
```bash
pbtk strings levenshtein-distance --string1 "hello" --string2 "world"
```

#### abjadify
```
--in-file <path>    Input file path (required)
--out-file <path>   Output file path (required)
```
```bash
pbtk strings abjadify --in-file article.txt --out-file article-abjad.txt
```

#### flesch-reading-ease
```
--in-file <path>    Input plain-text file path (required)
```
```bash
pbtk strings flesch-reading-ease --in-file article.txt
```

#### report
Performs a full text analysis including word count, sentence averages, page count, Flesch Reading Ease score, reading time, and more. Provide either a file path or a direct string.
```
--in-file <path>    Input file path (one of --in-file or --string required)
--string <text>     Input text string (one of --in-file or --string required)
```
```bash
pbtk strings report --in-file article.txt
pbtk strings report --string "The quick brown fox jumps over the lazy dog."
```

---

### interpreters

#### brainfuck
```
--file <path>   Path to the Brainfuck program file (required)
```
```bash
pbtk interpreters brainfuck --file hello.bf
```

#### ook
```
--file <path>   Path to the Ook program file (required)
```
```bash
pbtk interpreters ook --file hello.ook
```

#### bf-to-ook
```
--source <path>   Path to the source Brainfuck file (required)
--output <path>   Path to the output Ook file (required)
```
```bash
pbtk interpreters bf-to-ook --source hello.bf --output hello.ook
```

---

### images

#### steganography
```
--mode <encode|decode>   Operation mode (required)
--image <path>           Input image file path (required)
--message <text>         Message to encode (required for encode mode)
--output <path>          Output image file path (required for encode mode)
```
```bash
pbtk images steganography --mode encode --image photo.jpg --message "Secret" --output encoded.png
pbtk images steganography --mode decode --image encoded.png
```

---

### web

#### serve-html
```
--file <path>    Path to the HTML file to serve (required)
--port <port>    Port to listen on (default: 8080)
```
```bash
pbtk web serve-html --file index.html --port 8080
```

---

### openai

Requires `OPENAI_API_KEY` environment variable.

All commands automatically persist conversation history to a SQLite database at `~/.pbtk/history.db`. Use `openai history` to inspect and manage sessions.

#### chat
```
--message <text>      Message to send (required)
--session-id <id>     Session ID to continue a previous conversation (optional, omit to start new)
--model <name>        OpenAI model to use (default: gpt-5-nano)
```
```bash
pbtk openai chat --message "Hello"
pbtk openai chat --message "Continue our conversation" --session-id 42
```
The session ID is written to stderr after each call so it can be captured for follow-up messages.

#### translate
```
--text <text>               Text to translate (required)
--target-language <lang>    Target language (required)
--model <name>              OpenAI model to use (default: gpt-5-nano)
```
```bash
pbtk openai translate --text "Hello, how are you?" --target-language "Spanish"
```

#### ocaaar
```
--image-path <path>   Path to the image file (required)
--model <name>        OpenAI model to use (default: gpt-5-nano)
```
```bash
pbtk openai ocaaar --image-path poster.jpg
```

#### corpospeak
```
--source <text|path>          Source text or file path (required)
--audience <name>             Target audience (required): csuite, engineering, product, sales,
                              marketing, operations, finance, legal, hr, customer-success
--user-messages <text|path>   Writing style examples (optional, multiple values allowed)
--model <name>                OpenAI model to use (default: gpt-5-nano)
```
```bash
pbtk openai corpospeak --source "API performance is great" --audience "csuite"
pbtk openai corpospeak --source update.txt --audience "engineering" --user-messages "Hey team" "Let's ship this"
```

#### history list
Lists all stored sessions with their ID, command, creation time, and token usage.
```bash
pbtk openai history list
```

#### history delete
Deletes a session and all its stored messages.
```
--session-id <id>   ID of the session to delete (required)
```
```bash
pbtk openai history delete --session-id 42
```

---

### oauth

> OAuth authorities and discovered token endpoints must be absolute HTTPS URIs. The discovered token endpoint host must match the configured authority host before credentials are sent.

#### token
```
--profile <name>   Name of the OAuth profile to use (required)
```
```bash
pbtk oauth token --profile my-profile
```

#### profile add
```
--name <name>              Profile name (required)
--authority <uri>          OAuth authority URI (required)
--client-id <id>           OAuth client ID (required)
--client-secret <secret>   OAuth client secret (optional, prompted if omitted)
--scope <scope>            OAuth scope (optional)
```
```bash
pbtk oauth profile add --name my-profile --authority https://login.example.com/tenant --client-id abc123
```

#### profile update
```
--name <name>              Profile name to update (required)
--authority <uri>          New authority URI (optional)
--client-id <id>           New client ID (optional)
--client-secret <secret>   New client secret (optional)
--scope <scope>            New scope (optional)
```

#### profile delete
```
--name <name>   Profile name to delete (required)
```

---

### crypto

Homomorphic encryption using the Paillier cryptosystem. Encrypted numbers support additive operations without decryption.

> `generate-key` writes separate public and private key files. The public key file is safe to share; the private key file must be kept secret.

#### generate-key
```
--public-key-file <path>    Path to write the public key JSON file (required)
--private-key-file <path>   Path to write the private key JSON file (required)
```
```bash
pbtk crypto generate-key --public-key-file my.pub --private-key-file my.key
```

#### encrypt
```
--number <int>              Non-negative integer to encrypt (required)
--public-key-file <path>    Path to the public key JSON file (required)
--out-file <path>           Path to write the encrypted number JSON file (required)
```
```bash
pbtk crypto encrypt --number 37 --public-key-file my.pub --out-file a.enc
```

#### decrypt
```
--in-file <path>             Path to the encrypted number JSON file (required)
--private-key-file <path>    Path to the private key JSON file (required)
```
```bash
pbtk crypto decrypt --in-file a.enc --private-key-file my.key
```

#### add
```
--in-file1 <path>   First encrypted number JSON file (required)
--in-file2 <path>   Second encrypted number JSON file (required)
--out-file <path>   Path to write the encrypted sum JSON file (required)
```
```bash
pbtk crypto add --in-file1 a.enc --in-file2 b.enc --out-file sum.enc
```

#### subtract
```
--in-file1 <path>   Minuend encrypted number JSON file (required)
--in-file2 <path>   Subtrahend encrypted number JSON file (required)
--out-file <path>   Path to write the encrypted difference JSON file (required)
```
```bash
pbtk crypto subtract --in-file1 a.enc --in-file2 b.enc --out-file diff.enc
```

#### multiply
```
--in-file <path>    Encrypted number JSON file (required)
--scalar <int>      Non-negative plaintext scalar to multiply by (required)
--out-file <path>   Path to write the encrypted product JSON file (required)
```
```bash
pbtk crypto multiply --in-file a.enc --scalar 6 --out-file product.enc
```

#### encrypt-string
```
--string <text>             Plaintext string to encrypt, max 100 characters (required)
--public-key-file <path>    Path to the public key JSON file (required)
--out-file <path>           Path to write the encrypted string JSON file (required)
```
```bash
pbtk crypto encrypt-string --string "hello world" --public-key-file my.pub --out-file msg.estr
```

#### decrypt-string
```
--in-file <path>             Path to the encrypted string JSON file (required)
--private-key-file <path>    Path to the private key JSON file (required)
```
```bash
pbtk crypto decrypt-string --in-file msg.estr --private-key-file my.key
```

#### replace
```
--in-file <path>        Encrypted string JSON file (required)
--start <index>         Zero-based index of the first character to replace (required)
--replacement <text>    Plaintext replacement characters (required)
--out-file <path>       Path to write the updated encrypted string JSON file (required)
```
```bash
pbtk crypto replace --in-file msg.estr --start 6 --replacement "there" --out-file updated.estr
```

#### substring
```
--in-file <path>    Encrypted string JSON file (required)
--start <index>     Zero-based index of the first character to include (required)
--length <count>    Number of characters to include (optional, defaults to remainder)
--out-file <path>   Path to write the encrypted substring JSON file (required)
```
```bash
pbtk crypto substring --in-file msg.estr --start 6 --length 5 --out-file sub.estr
```

---

## Requirements

- .NET 9.0
- OpenAI API key (for `openai` commands)

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This project is a CLI toolkit exposing varied functionality organized by topic.
CLI arguments follow a topic-action pattern: `[topic] [action] [options]`.
For example, the CLI arguments "strings reverse --in-file hw.txt --out-file hw-reversed.txt" would read the content of in-file and output the reversed string to the path of out-file.

The project is .NET 9, and uses Microsoft's System.CommandLine library for building argument sets.

## Development Workflow

### Feature Branch Process

**CRITICAL - ALWAYS CHECK BRANCH FIRST**: All new features must be developed in feature branches:
- **BEFORE starting any work**: Check current branch with `git status` or `git branch`
- **Create a new branch for each feature**: `git checkout -b feature/feature-name`
- **Feature branch names must be prefixed with `feature/`**
- **If you're in the middle of another feature, NEVER start a new one**
- **Complete the current feature branch before starting any new work**
- **Merge feature branches back to master when complete**
- **NEVER add unrelated features to existing feature branches**

**Branch Workflow Checklist**:
1. Check current branch status
2. If on existing feature branch, complete that work first
3. Create new feature branch for new work
4. Implement feature on correct branch
5. Test and validate changes
6. Create PR when feature is complete

### Version Management

**CRITICAL**: PRs that modify the main project code must include a package version bump following semantic versioning (SemVer):
- **PATCH** (x.x.X): Bug fixes, minor internal changes, non-breaking updates
- **MINOR** (x.X.x): New features, new functionality, backward-compatible changes
- **MAJOR** (X.x.x): Breaking changes, API changes that require user action
- Update the `<Version>` element in `Pixelbadger.Toolkit.csproj`
- PR validation will fail if version has not been incremented from the published NuGet package

**Version bumps are NOT required for**:
- Internal documentation changes (CLAUDE.md, code comments)
- Test-only changes (adding/modifying files in `Pixelbadger.Toolkit.Tests/`)
- GitHub Actions workflow changes (`.github/` folder)
- Repository configuration files (.gitignore, .editorconfig, etc.)

**Note**: README.md changes DO require version bumps as the README is published with the NuGet package.

### README Maintenance

**CRITICAL**: All new commands, topics, or modifications to existing functionality must trigger a README review and update:
- **New topics or actions**: Add to table of contents and create detailed sections with usage examples
- **Modified commands**: Update existing documentation, examples, and option descriptions
- **Ensure consistency**: README examples must match CLAUDE.md examples and actual command behavior
- **Complete documentation**: Include usage, options, examples, and technical details for each command
- README updates are mandatory for any PR that adds or modifies CLI functionality

### Development Commands

- **Build**: `dotnet build`
- **Test**: `dotnet test`
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
  - `pbtk openai corpospeak --source "API performance is great" --audience "csuite"`
  - `pbtk openai corpospeak --source "New feature deployed" --audience "engineering" --user-messages "Hey team" "Let's ship this"`

### Testing Requirements

**CRITICAL**: All new functionality must include comprehensive unit tests following these requirements:

#### Test Coverage Standards
- **New Components**: All new component classes must have corresponding test classes with 100% method coverage
- **New Commands**: All new command actions must have integration tests verifying proper option handling and error cases
- **Refactoring**: When refactoring existing code, ensure tests exist or add them before refactoring
- **Bug Fixes**: All bug fixes must include regression tests that would have caught the original issue

#### Testing Framework and Tools
- **Framework**: xUnit for all unit and integration tests
- **Assertions**: FluentAssertions for improved readability and better error messages
- **Mocking**: Moq for dependency mocking, especially for external service dependencies
- **Test Project**: `Pixelbadger.Toolkit.Tests` - all tests must be in this project

#### Test Organization and Naming
- **Test Classes**: Named `{ComponentName}Tests.cs` (e.g., `ChatComponentTests.cs`)
- **Test Methods**: Use descriptive names following pattern `MethodName_Should{ExpectedBehavior}_When{Condition}`
- **Categories**: Use `[Theory]` and `[InlineData]` for parametrized tests with multiple scenarios
- **Cleanup**: Implement `IDisposable` for tests requiring resource cleanup (files, directories, etc.)

#### Test Quality Requirements
- **Isolation**: Each test must be independent and not rely on other tests
- **Deterministic**: Tests must produce consistent results across different environments
- **Fast Execution**: Tests should complete quickly (< 1 second per test preferred)
- **Comprehensive Coverage**: Test success paths, edge cases, error conditions, and security concerns

#### Mocking and Dependency Injection
- **Interface-Based Design**: All testable components must use dependency injection with interfaces
- **Service Mocking**: External services (OpenAI, file system, network) must be mockable via interfaces
- **Constructor Injection**: Components should receive dependencies through constructor injection
- **Example Pattern**:
  ```csharp
  public class ExampleComponentTests
  {
      private readonly Mock<IExternalService> _mockService;
      private readonly ExampleComponent _component;

      public ExampleComponentTests()
      {
          _mockService = new Mock<IExternalService>();
          _component = new ExampleComponent(_mockService.Object);
      }
  }
  ```

#### File and Resource Handling in Tests
- **Temporary Files**: Use `Path.GetTempPath()` and `Guid.NewGuid()` for unique test directories
- **Cleanup**: Always clean up temporary files and directories in test disposal
- **Test Assets**: Store reusable test files in `test-assets/` folder (gitignored)
- **Example Pattern**:
  ```csharp
  public class FileHandlingTests : IDisposable
  {
      private readonly string _testDirectory;

      public FileHandlingTests()
      {
          _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
          Directory.CreateDirectory(_testDirectory);
      }

      public void Dispose()
      {
          if (Directory.Exists(_testDirectory))
              Directory.Delete(_testDirectory, true);
      }
  }
  ```

#### Test Categories and Scenarios
1. **Success Path Tests**: Verify normal operation with valid inputs
2. **Edge Case Tests**: Empty inputs, boundary values, special characters
3. **Error Condition Tests**: Invalid inputs, missing files, network failures
4. **Security Tests**: Input validation, XML escaping, prompt injection protection
5. **Performance Tests**: Large inputs, memory usage, timeout scenarios
6. **Integration Tests**: End-to-end command execution and option parsing

#### Test Execution and CI/CD
- **Local Development**: Run `dotnet test` before committing changes
- **PR Validation**: All tests must pass before PR can be merged
- **Coverage Reporting**: Maintain high test coverage (target 90%+ for new code)
- **Performance**: Test suite should complete in under 30 seconds

#### Example Test Structure
```csharp
[Fact]
public async Task ComponentMethod_ShouldReturnExpectedResult_WhenValidInputProvided()
{
    // Arrange
    var input = "test input";
    var expectedOutput = "expected result";
    _mockService.Setup(x => x.ProcessAsync(input)).ReturnsAsync(expectedOutput);

    // Act
    var result = await _component.ProcessAsync(input);

    // Assert
    result.Should().Be(expectedOutput);
    _mockService.Verify(x => x.ProcessAsync(input), Times.Once);
}
```

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
- **openai**: chat, translate, ocaaar, corpospeak

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
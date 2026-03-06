# AGENTS.md — AI Agent Instructions for dotnet-coding-context-mcp

## Project Overview

MCP server that gives AI agents coding context for writing compilable C# code — type signatures, constructors, mock patterns, and test conventions via Roslyn analysis. Ships as a dotnet tool (`DotnetCodingContextMCP`) on NuGet.

## Build & Test

```bash
dotnet build
dotnet test
```

- Targets **net8.0** and **net10.0**.
- CI runs on **ubuntu-latest** and **windows-latest** (see `.github/workflows/ci.yml`).
- Requires MSBuild (included with .NET SDK) for Roslyn workspace loading.

## Architecture

### Tool Pattern

Each MCP tool is a `public static async` method in a sealed class under `src/DotnetCodingContextMCP/Tools/`:

```csharp
[McpServerToolType]
public sealed class MyTool
{
    [McpServerTool(Name = "tool_name")]
    [Description("Description — lead with what the agent should do BEFORE taking action.")]
    public static async Task<string> MyMethodAsync(
        RoslynWorkspaceService workspace,    // DI-injected
        [Description("...")] string param1,
        CancellationToken cancellationToken = default)
    {
        // 1. Load workspace if path provided
        // 2. Analyze with Roslyn
        // 3. Return JSON
    }
}
```

### Key Services

- **`RoslynWorkspaceService`** — singleton wrapping `MSBuildWorkspace`. Caches loaded solutions by path. Provides type lookup, interface member extraction, mock setup generation, and instantiation hints.
- **`TestFrameworkDetector`** — static helper for detecting test framework, assertion library, and mocking library from package references.

### Mock Setup Generation

The server generates mock patterns for **three libraries**:
- **Moq**: `mock.Setup(x => x.Method(It.IsAny<T>())).Returns(...)`
- **NSubstitute**: `substitute.Method(Arg.Any<T>()).Returns(...)`
- **FakeItEasy**: `A.CallTo(() => fake.Method(A<T>._)).Returns(...)`

The mocking library is auto-detected from the test project's package references.

## Testing

- Unit tests use **xUnit**.
- Tests cover framework detection, mock pattern generation, and workspace caching.
- Test naming: `MethodName_Scenario_ExpectedBehavior`.

## Key Files

| File | Purpose |
|------|---------|
| `src/.../Services/RoslynWorkspaceService.cs` | Core Roslyn analysis with workspace caching |
| `src/.../Tools/TestFrameworkDetector.cs` | Test framework/assertion/mock library detection |
| `src/.../Tools/CodingContextTool.cs` | `get_coding_context` — type signatures + constructors |
| `src/.../Tools/InterfaceContractTool.cs` | `get_interface_contract` — mock-ready interface extraction |
| `src/.../Tools/TestContextTool.cs` | `get_project_test_context` — test convention detection |
| `src/.../Tools/SolutionAnalysisTool.cs` | `analyze_solution_for_testing` — testable type discovery |
| `src/.../Tools/ProjectContextTool.cs` | `get_testable_types_with_context` — compound one-call tool |
| `src/.../Models/CodingContextModels.cs` | All response model records |

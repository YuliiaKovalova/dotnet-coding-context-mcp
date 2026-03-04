# DotnetCodingContextMCP

An MCP server that gives AI agents the **coding context** they need to write compilable C# code — type signatures, constructors, mock patterns, and test conventions in a single call.

## The Problem

When AI agents write C# code, they hallucinate type names, property names, constructor signatures, and assertion patterns:

| What the agent writes | What actually exists |
|---|---|
| `new UserRepository()` | `new UserRepository(IDbContext context)` |
| `user.EmailAddress = "..."` | `user.Email` (different property name) |
| `record.Name = "x"` | positional record — requires constructor |
| `result.Should().BeTrue()` | `result.ShouldBeTrue()` (wrong assertion library) |

This causes build-fail-fix-build spirals that waste time and tokens.

## The Solution: 4 Tools

### `analyze_solution_for_testing` *(new in v0.2.0)*

Discover what's testable in a solution — call this FIRST.

```
Input:  { "path": "/src/MyApp.sln" }

Output:
{
  "sourceProjects": ["MyApp.Core", "MyApp.API"],
  "testProjects": ["MyApp.Tests"],
  "projects": [
    {
      "projectName": "MyApp.Core",
      "totalPublicTypes": 42,
      "testableTypes": [
        {
          "name": "UserService",
          "kind": "class",
          "publicMethodCount": 8,
          "constructorDependencies": ["IUserRepository", "ILogger<UserService>"],
          "complexity": 12,
          "instantiationHint": "new UserService(Mock.Of<IUserRepository>(), Mock.Of<ILogger<UserService>>())"
        }
      ]
    }
  ],
  "recommendedTestSetup": "Most-used dependencies to mock: IUserRepository, ILogger. Create mock instances first."
}
```

### `get_coding_context`

One call → everything needed to write code against specific types.

```
Input:  { "typeNames": "UserService, OrderRepository",
          "path": "/src/MyApp.sln" }

Output:
{
  "types": [
    {
      "name": "UserService",
      "using": "using MyApp.Services;",
      "kind": "class",
      "constructors": ["new UserService(IUserRepository repo, ILogger<UserService> logger)"],
      "properties": [
        { "name": "IsInitialized", "type": "bool", "settable": false }
      ],
      "instantiation": "new UserService(Mock.Of<IUserRepository>(), Mock.Of<ILogger<UserService>>())"
    }
  ],
  "relatedTypes": [...]
}
```

### `get_interface_contract`

Mock-ready interface extraction with Moq setup patterns.

```
Input:  { "interfaceName": "IUserRepository", "path": "/src/MyApp.sln" }

Output:
{
  "members": [
    {
      "name": "GetByIdAsync",
      "signature": "Task<User?> GetByIdAsync(int id)",
      "mockSetup": "mock.Setup(x => x.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(default(User?)!)"
    }
  ],
  "implementations": ["SqlUserRepository", "InMemoryUserRepository"],
  "parameterTypes": [{ "name": "User", "kind": "class", "instantiation": "..." }]
}
```

### `get_project_test_context`

Analyze existing tests to extract conventions.

```
Input:  { "testProjectPath": "/test/MyApp.Tests/MyApp.Tests.csproj" }

Output:
{
  "framework": "xunit",
  "assertionLibrary": "Shouldly",
  "mockingLibrary": "Moq",
  "assertionPatterns": {
    "equality": "result.ShouldBe(expected)",
    "true": "result.ShouldBeTrue()",
    "null": "result.ShouldBeNull()"
  },
  "namingConvention": "MethodName_Scenario_ExpectedBehavior",
  "helperMethods": [
    { "name": "CreateTestUser", "signature": "User CreateTestUser(string name)" }
  ]
}
```

## Quick Start

### Install

```bash
dotnet tool install --global DotnetCodingContextMCP
```

### VS Code / GitHub Copilot

Add to `mcp.json`:

```json
{
  "servers": {
    "coding-context": {
      "type": "stdio",
      "command": "coding-context-mcp"
    }
  }
}
```

### Pair with DotnetTemplateMCP

For complete .NET AI tooling — templates for project creation, coding context for code authoring:

```json
{
  "servers": {
    "dotnet-templates": {
      "type": "stdio",
      "command": "template-engine-mcp"
    },
    "coding-context": {
      "type": "stdio",
      "command": "coding-context-mcp"
    }
  }
}
```

## Why Not SharpLensMcp / Other Roslyn MCPs?

Existing Roslyn MCP servers expose many granular tools designed for **code exploration** (navigate, inspect, analyze). This MCP exposes **4 compound tools** designed for **code authoring** — giving agents exactly what they need to write compilable code:

| | Granular Roslyn MCPs | This MCP (4 tools) |
|---|---|---|
| **Tool calls per coding task** | ~25-50 | **3-5** |
| **Tool selection complexity** | High — dozens of choices | Low — 3 obvious choices |
| **Output format** | Raw Roslyn data | Ready-to-paste code snippets |
| **Mock setup generation** | ❌ | ✅ Moq patterns included |
| **Instantiation examples** | ❌ | ✅ with actual constructor args |
| **Test convention detection** | ❌ | ✅ framework, assertions, helpers |

## Requirements

- .NET 10 SDK
- MSBuild (included with .NET SDK)

## License

MIT

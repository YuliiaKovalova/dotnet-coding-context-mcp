# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.0] - 2026-03-06

### Added
- **Multi-target net8.0 + net10.0** — broadens adoption beyond .NET 10 preview users
- **Unit tests** — test project with xUnit covering framework detection, mock patterns, workspace caching
- **CI pipeline** — GitHub Actions build+test on Ubuntu and Windows for both frameworks
- **Dependabot** — automated weekly dependency updates for NuGet and GitHub Actions
- **NSubstitute mock patterns** — `substitute.Method(Arg.Any<T>()).Returns(...)` generation
- **FakeItEasy mock patterns** — `A.CallTo(() => fake.Method(A<T>._)).Returns(...)` generation
- **Workspace caching** — avoids recompiling the solution when the same path is used across tool calls
- **MSBuild error logging** — `WorkspaceFailed` events now logged as warnings instead of silently swallowed
- **CHANGELOG.md** — this file
- **AGENTS.md** — development conventions for AI agents

### Changed
- **Upgraded MCP SDK** from `ModelContextProtocol` 0.2.0-preview.2 to 1.0.0
- Extracted `TestFrameworkDetector` into a separate testable class

### Fixed
- `Program.cs` now uses MCP SDK 1.0.0 `AddMcpServer(options => ...)` API with proper ServerInfo

## [0.3.3] - 2026-03-05

### Fixed
- Filter type enumeration to source-defined types only (replace namespace string matching with `IsInSource`)

## [0.3.2] - 2026-03-05

### Changed
- Version bump

## [0.3.1] - 2026-03-04

### Added
- Initial public release with 5 tools: `analyze_solution_for_testing`, `get_testable_types_with_context`, `get_coding_context`, `get_interface_contract`, `get_project_test_context`
- Roslyn-powered type analysis with constructor, property, and method extraction
- Moq mock setup pattern generation
- Test convention detection (framework, assertion library, mocking library, naming patterns)
- Instantiation hint generation with mock dependencies
- Test hint generation based on type structure

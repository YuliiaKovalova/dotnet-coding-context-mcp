using System.Text.Json.Serialization;

namespace DotnetCodingContextMCP.Models;

public sealed record TypeCodingContext
{
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required string Using { get; init; }
    public required string Kind { get; init; }
    public required IReadOnlyList<string> Constructors { get; init; }
    public required IReadOnlyList<PropertyInfo> Properties { get; init; }
    public required IReadOnlyList<MethodInfo> PublicMethods { get; init; }
    public required string Instantiation { get; init; }
    public required IReadOnlyList<string> Interfaces { get; init; }
    public string? BaseType { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsSealed { get; init; }
}

public sealed record PropertyInfo
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required bool Settable { get; init; }
    public bool IsRequired { get; init; }
}

public sealed record MethodInfo
{
    public required string Name { get; init; }
    public required string ReturnType { get; init; }
    public required string Signature { get; init; }
}

public sealed record CodingContextResponse
{
    public required IReadOnlyList<TypeCodingContext> Types { get; init; }
    public required IReadOnlyList<TypeCodingContext> RelatedTypes { get; init; }
}

public sealed record InterfaceMember
{
    public required string Name { get; init; }
    public required string ReturnType { get; init; }
    public required string Signature { get; init; }
    public required string MockSetup { get; init; }
    public TypeCodingContext? ReturnTypeContext { get; init; }
}

public sealed record InterfaceContractResponse
{
    public required string InterfaceName { get; init; }
    public required string FullName { get; init; }
    public required string Using { get; init; }
    public required IReadOnlyList<InterfaceMember> Members { get; init; }
    public required IReadOnlyList<string> Implementations { get; init; }
    public required IReadOnlyList<TypeCodingContext> ParameterTypes { get; init; }
}

public sealed record TestContextResponse
{
    public required string Framework { get; init; }
    public required string AssertionLibrary { get; init; }
    public required string MockingLibrary { get; init; }
    public required IReadOnlyList<string> TargetFrameworks { get; init; }
    public required IReadOnlyList<string> CommonUsings { get; init; }
    public required IDictionary<string, string> AssertionPatterns { get; init; }
    public required string NamingConvention { get; init; }
    public required IReadOnlyList<HelperMethodInfo> HelperMethods { get; init; }
    public required IReadOnlyList<BaseClassInfo> BaseClasses { get; init; }
}

public sealed record HelperMethodInfo
{
    public required string Name { get; init; }
    public required string File { get; init; }
    public required string Signature { get; init; }
}

public sealed record BaseClassInfo
{
    public required string Name { get; init; }
    public required string File { get; init; }
}

public sealed record TestableTypeInfo
{
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required string Kind { get; init; }
    public required string Namespace { get; init; }
    public required int PublicMethodCount { get; init; }
    public required int PublicPropertyCount { get; init; }
    public required IReadOnlyList<string> ConstructorDependencies { get; init; }
    public required int Complexity { get; init; }
    public required string InstantiationHint { get; init; }
}

public sealed record TestableTypeWithContext
{
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required string Kind { get; init; }
    public required string Namespace { get; init; }
    public required string Using { get; init; }
    public required IReadOnlyList<string> Constructors { get; init; }
    public required IReadOnlyList<PropertyInfo> Properties { get; init; }
    public required IReadOnlyList<MethodInfo> PublicMethods { get; init; }
    public required string Instantiation { get; init; }
    public required IReadOnlyList<string> Interfaces { get; init; }
    public required IReadOnlyList<InterfaceMockSetup> MockSetups { get; init; }
    public required IReadOnlyList<string> TestHints { get; init; }
}

public sealed record InterfaceMockSetup
{
    public required string InterfaceName { get; init; }
    public required IReadOnlyList<InterfaceMember> Members { get; init; }
}

public sealed record ProjectTestSummary
{
    public required string ProjectName { get; init; }
    public required int TotalPublicTypes { get; init; }
    public required IReadOnlyList<TestableTypeInfo> TestableTypes { get; init; }
}

public sealed record ProjectTestContextWithTypes
{
    public required string ProjectName { get; init; }
    public required IReadOnlyList<TestableTypeWithContext> Types { get; init; }
    public required TestContextResponse? TestConventions { get; init; }
    public required string RecommendedTestSetup { get; init; }
}

public sealed record SolutionTestAnalysis
{
    public required string SolutionPath { get; init; }
    public required IReadOnlyList<string> SourceProjects { get; init; }
    public required IReadOnlyList<string> TestProjects { get; init; }
    public required IReadOnlyList<ProjectTestSummary> Projects { get; init; }
    public required string RecommendedTestSetup { get; init; }
}

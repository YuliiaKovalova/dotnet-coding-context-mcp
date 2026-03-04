using System.ComponentModel;
using System.Text.Json;
using DotnetCodingContextMCP.Models;
using DotnetCodingContextMCP.Services;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotnetCodingContextMCP.Tools;

[McpServerToolType]
public sealed class SolutionAnalysisTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "analyze_solution_for_testing")]
    [Description("Analyze a .NET solution to discover what's testable. Returns public types grouped by project with their complexity (method count, dependency count), existing test coverage gaps, and recommended testing priorities. Call this FIRST before writing any tests — it tells you what to test and how to set up each type.")]
    public static async Task<string> AnalyzeSolutionForTestingAsync(
        RoslynWorkspaceService workspace,
        [Description("Path to .sln, .slnx, .csproj, or directory containing the solution")] string path,
        [Description("Maximum number of types to return per project (default 30). Use a lower number for large solutions.")] int maxTypesPerProject = 30,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var solution = await workspace.LoadSolutionAsync(path, cancellationToken);

            var projectSummaries = new List<ProjectTestSummary>();
            var testProjects = new List<string>();
            var sourceProjects = new List<string>();

            foreach (var project in solution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool isTestProject = IsTestProject(project);
                if (isTestProject)
                {
                    testProjects.Add(project.Name);
                    continue;
                }

                sourceProjects.Add(project.Name);
                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation is null) continue;

                var testableTypes = new List<TestableTypeInfo>();
                var allTypes = GetAllNamedTypes(compilation.GlobalNamespace)
                    .Where(t => t.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
                    .Where(t => !t.IsAbstract || t.TypeKind == TypeKind.Interface)
                    .Where(t => t.ContainingNamespace?.ToDisplayString() is not null
                                and not "System" and not "Microsoft")
                    .ToList();

                foreach (var type in allTypes)
                {
                    int methodCount = type.GetMembers().OfType<IMethodSymbol>()
                        .Count(m => m.MethodKind == MethodKind.Ordinary &&
                                    m.DeclaredAccessibility == Accessibility.Public);
                    int propertyCount = type.GetMembers().OfType<IPropertySymbol>()
                        .Count(p => p.DeclaredAccessibility == Accessibility.Public);
                    int ctorParamCount = type.InstanceConstructors
                        .Where(c => c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
                        .Select(c => c.Parameters.Length)
                        .DefaultIfEmpty(0)
                        .Max();

                    // Skip trivial types with no public surface
                    if (methodCount == 0 && propertyCount == 0) continue;

                    var interfaceDeps = type.InstanceConstructors
                        .SelectMany(c => c.Parameters)
                        .Where(p => p.Type.TypeKind == TypeKind.Interface)
                        .Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                        .Distinct()
                        .ToList();

                    int complexity = methodCount + (ctorParamCount * 2);

                    testableTypes.Add(new TestableTypeInfo
                    {
                        Name = type.Name,
                        FullName = type.ToDisplayString(),
                        Kind = GetTypeKind(type),
                        Namespace = type.ContainingNamespace.ToDisplayString(),
                        PublicMethodCount = methodCount,
                        PublicPropertyCount = propertyCount,
                        ConstructorDependencies = interfaceDeps,
                        Complexity = complexity,
                        InstantiationHint = workspace.GenerateInstantiationHint(type),
                    });
                }

                // Sort by complexity (most testable first) and take top N
                testableTypes = testableTypes
                    .OrderByDescending(t => t.Complexity)
                    .Take(maxTypesPerProject)
                    .ToList();

                if (testableTypes.Count > 0)
                {
                    projectSummaries.Add(new ProjectTestSummary
                    {
                        ProjectName = project.Name,
                        TotalPublicTypes = allTypes.Count,
                        TestableTypes = testableTypes,
                    });
                }
            }

            var response = new SolutionTestAnalysis
            {
                SolutionPath = path,
                SourceProjects = sourceProjects,
                TestProjects = testProjects,
                Projects = projectSummaries,
                RecommendedTestSetup = GenerateTestSetupRecommendation(projectSummaries),
            };

            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = "analysis_failed", message = ex.Message }, JsonOptions);
        }
    }

    private static bool IsTestProject(Project project)
    {
        var name = project.Name;
        if (name.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Spec", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check package references from project file
        var refs = project.MetadataReferences.Select(r => r.Display ?? "").ToList();
        return refs.Any(r => r.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                             r.Contains("nunit", StringComparison.OrdinalIgnoreCase) ||
                             r.Contains("mstest", StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateTestSetupRecommendation(List<ProjectTestSummary> projects)
    {
        var allDeps = projects
            .SelectMany(p => p.TestableTypes)
            .SelectMany(t => t.ConstructorDependencies)
            .GroupBy(d => d)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        if (allDeps.Count == 0)
            return "Most types have simple constructors — use direct instantiation with new().";

        return $"Most-used dependencies to mock: {string.Join(", ", allDeps)}. " +
               "Create mock instances for these first, then pass to constructors.";
    }

    private static string GetTypeKind(INamedTypeSymbol type)
    {
        if (type.IsRecord) return type.IsValueType ? "record struct" : "record";
        if (type.TypeKind == TypeKind.Interface) return "interface";
        if (type.TypeKind == TypeKind.Enum) return "enum";
        if (type.TypeKind == TypeKind.Struct) return "struct";
        if (type.IsAbstract) return "abstract class";
        if (type.IsSealed) return "sealed class";
        return "class";
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
            yield return type;
        foreach (var childNs in ns.GetNamespaceMembers())
            foreach (var type in GetAllNamedTypes(childNs))
                yield return type;
    }
}

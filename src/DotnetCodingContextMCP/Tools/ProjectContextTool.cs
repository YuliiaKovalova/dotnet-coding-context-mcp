using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotnetCodingContextMCP.Models;
using DotnetCodingContextMCP.Services;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotnetCodingContextMCP.Tools;

[McpServerToolType]
public sealed class ProjectContextTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_testable_types_with_context")]
    [Description("ONE call to get everything needed to write tests for a project — all public types with their exact constructors, properties, method signatures, instantiation examples, Moq mock setups for interface dependencies, AND per-type test hints (null checks, boundary values, state transitions, exception paths). Also detects test conventions. Call this INSTEAD of calling analyze_solution_for_testing + get_coding_context + get_interface_contract separately.")]
    public static async Task<string> GetTestableTypesWithContextAsync(
        RoslynWorkspaceService workspace,
        [Description("Path to .sln, .slnx, .csproj, or directory")] string path,
        [Description("Filter to a specific project name or namespace pattern (e.g. 'Ordering.Domain', 'Basket'). Leave empty for all projects.")] string? projectFilter = null,
        [Description("Maximum types to return (default 20). Types are ranked by complexity.")] int maxTypes = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var solution = await workspace.LoadSolutionAsync(path, cancellationToken);

            var typesWithContext = new List<TestableTypeWithContext>();
            TestContextResponse? testConventions = null;
            var processedFullNames = new HashSet<string>();
            string? testProjectPath = null;

            foreach (var project in solution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsTestProject(project))
                {
                    // Capture the first test project path for convention detection
                    testProjectPath ??= project.FilePath;
                    continue;
                }

                // Apply project filter
                if (projectFilter is not null &&
                    !project.Name.Contains(projectFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation is null) continue;

                var allTypes = GetAllNamedTypes(compilation.GlobalNamespace)
                    .Where(t => t.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
                    .Where(t => !t.IsAbstract || t.TypeKind == TypeKind.Interface)
                    .Where(t => t.Locations.Any(l => l.IsInSource))
                    .ToList();

                foreach (var type in allTypes)
                {
                    var fullName = type.ToDisplayString();
                    if (!processedFullNames.Add(fullName)) continue;

                    // Skip interfaces that aren't constructor dependencies
                    if (type.TypeKind == TypeKind.Interface) continue;

                    int methodCount = type.GetMembers().OfType<IMethodSymbol>()
                        .Count(m => m.MethodKind == MethodKind.Ordinary &&
                                    m.DeclaredAccessibility == Accessibility.Public);
                    int propertyCount = type.GetMembers().OfType<IPropertySymbol>()
                        .Count(p => p.DeclaredAccessibility == Accessibility.Public);

                    if (methodCount == 0 && propertyCount == 0) continue;

                    var codingContext = workspace.BuildTypeCodingContext(type);

                    // Build mock setups for interface dependencies
                    var mockSetups = new List<InterfaceMockSetup>();
                    var seenInterfaces = new HashSet<string>();

                    foreach (var ctor in type.InstanceConstructors)
                    {
                        foreach (var param in ctor.Parameters)
                        {
                            if (param.Type is INamedTypeSymbol { TypeKind: TypeKind.Interface } iface &&
                                seenInterfaces.Add(iface.ToDisplayString()))
                            {
                                var members = workspace.ExtractInterfaceMembers(iface);
                                mockSetups.Add(new InterfaceMockSetup
                                {
                                    InterfaceName = iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                    Members = members,
                                });
                            }
                        }
                    }

                    int complexity = methodCount + (type.InstanceConstructors
                        .Select(c => c.Parameters.Length)
                        .DefaultIfEmpty(0).Max() * 2);

                    typesWithContext.Add(new TestableTypeWithContext
                    {
                        Name = codingContext.Name,
                        FullName = codingContext.FullName,
                        Kind = codingContext.Kind,
                        Namespace = type.ContainingNamespace.ToDisplayString(),
                        Using = codingContext.Using,
                        Constructors = codingContext.Constructors,
                        Properties = codingContext.Properties,
                        PublicMethods = codingContext.PublicMethods,
                        Instantiation = codingContext.Instantiation,
                        Interfaces = codingContext.Interfaces,
                        MockSetups = mockSetups,
                        TestHints = workspace.GenerateTestHints(type),
                    });
                }
            }

            // Sort by complexity and take top N
            typesWithContext = typesWithContext
                .OrderByDescending(t => t.PublicMethods.Count + (t.MockSetups.Count * 2))
                .Take(maxTypes)
                .ToList();

            // Detect test conventions if a test project exists
            if (testProjectPath is not null && File.Exists(testProjectPath))
            {
                testConventions = await DetectTestConventionsAsync(testProjectPath, cancellationToken);
            }

            // Generate setup recommendation
            var allDeps = typesWithContext
                .SelectMany(t => t.MockSetups)
                .Select(m => m.InterfaceName)
                .GroupBy(d => d)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => g.Key)
                .ToList();

            string setupRecommendation = allDeps.Count == 0
                ? "Most types have simple constructors — use direct instantiation."
                : $"Most-used dependencies to mock: {string.Join(", ", allDeps)}.";

            var response = new ProjectTestContextWithTypes
            {
                ProjectName = projectFilter ?? "all",
                Types = typesWithContext,
                TestConventions = testConventions,
                RecommendedTestSetup = setupRecommendation,
            };

            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = "analysis_failed", message = ex.Message }, JsonOptions);
        }
    }

    private static async Task<TestContextResponse?> DetectTestConventionsAsync(
        string testProjectPath, CancellationToken ct)
    {
        try
        {
            var doc = XDocument.Load(testProjectPath);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var packageRefs = doc.Root?.Descendants(ns + "PackageReference")
                .Select(pr => pr.Attribute("Include")?.Value)
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList() ?? [];

            var targetFramework = doc.Root?.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value;
            var targetFrameworks = doc.Root?.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value;
            var frameworks = (targetFrameworks ?? targetFramework ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

            var framework = DetectFramework(packageRefs, "xunit", "nunit", "MSTest", "TUnit");
            var assertionLib = DetectFramework(packageRefs, "Shouldly", "FluentAssertions") ?? framework switch
            {
                "xunit" => "xunit.Assert",
                "nunit" => "NUnit.Assert",
                _ => "unknown",
            };
            var mockingLib = DetectFramework(packageRefs, "Moq", "NSubstitute", "FakeItEasy") ?? "none";

            return new TestContextResponse
            {
                Framework = framework ?? "unknown",
                AssertionLibrary = assertionLib,
                MockingLibrary = mockingLib,
                TargetFrameworks = frameworks,
                CommonUsings = [],
                AssertionPatterns = new Dictionary<string, string>(),
                NamingConvention = "unknown",
                HelperMethods = [],
                BaseClasses = [],
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? DetectFramework(List<string> packages, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (packages.Any(p => p.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return null;
    }

    private static bool IsTestProject(Project project)
    {
        var name = project.Name;
        return name.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Spec", StringComparison.OrdinalIgnoreCase);
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

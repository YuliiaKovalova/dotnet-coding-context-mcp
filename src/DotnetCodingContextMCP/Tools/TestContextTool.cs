using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotnetCodingContextMCP.Models;
using DotnetCodingContextMCP.Services;
using ModelContextProtocol.Server;

namespace DotnetCodingContextMCP.Tools;

[McpServerToolType]
public sealed partial class TestContextTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_project_test_context")]
    [Description("BEFORE creating a test file, call this to discover the project's test conventions — which test framework (xunit/nunit/mstest), assertion library (Shouldly/FluentAssertions/Assert), mocking library (Moq/NSubstitute), naming patterns, and helper methods to reuse. Prevents using the wrong assertion syntax or framework attributes.")]
    public static async Task<string> GetProjectTestContextAsync(
        RoslynWorkspaceService workspace,
        [Description("Path to the test .csproj file")] string testProjectPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(testProjectPath))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "not_found",
                    message = $"Test project not found: {testProjectPath}",
                }, JsonOptions);
            }

            // Parse .csproj for package references
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

            // Detect framework and libraries
            var framework = DetectTestFramework(packageRefs);
            var assertionLib = DetectAssertionLibrary(packageRefs);
            var mockingLib = DetectMockingLibrary(packageRefs);

            // Scan test files for patterns
            var projectDir = Path.GetDirectoryName(testProjectPath)!;
            var testFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToList();

            var assertionPatterns = await DetectAssertionPatternsAsync(testFiles, assertionLib, cancellationToken);
            var namingConvention = await DetectNamingConventionAsync(testFiles, cancellationToken);
            var helpers = await FindHelperMethodsAsync(testFiles, cancellationToken);
            var baseClasses = await FindBaseClassesAsync(testFiles, cancellationToken);
            var commonUsings = await DetectCommonUsingsAsync(testFiles, cancellationToken);

            var response = new TestContextResponse
            {
                Framework = framework,
                AssertionLibrary = assertionLib,
                MockingLibrary = mockingLib,
                TargetFrameworks = frameworks,
                CommonUsings = commonUsings,
                AssertionPatterns = assertionPatterns,
                NamingConvention = namingConvention,
                HelperMethods = helpers,
                BaseClasses = baseClasses,
            };

            return JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = "analysis_failed", message = ex.Message }, JsonOptions);
        }
    }

    private static string DetectTestFramework(List<string> packages)
    {
        if (packages.Any(p => p.Equals("xunit", StringComparison.OrdinalIgnoreCase))) return "xunit";
        if (packages.Any(p => p.Equals("NUnit", StringComparison.OrdinalIgnoreCase))) return "nunit";
        if (packages.Any(p => p.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase))) return "mstest";
        if (packages.Any(p => p.StartsWith("TUnit", StringComparison.OrdinalIgnoreCase))) return "tunit";
        return "unknown";
    }

    private static string DetectAssertionLibrary(List<string> packages)
    {
        if (packages.Any(p => p.Equals("Shouldly", StringComparison.OrdinalIgnoreCase))) return "Shouldly";
        if (packages.Any(p => p.StartsWith("FluentAssertions", StringComparison.OrdinalIgnoreCase))) return "FluentAssertions";
        if (packages.Any(p => p.Equals("xunit", StringComparison.OrdinalIgnoreCase))) return "xunit.Assert";
        if (packages.Any(p => p.Equals("NUnit", StringComparison.OrdinalIgnoreCase))) return "NUnit.Assert";
        return "unknown";
    }

    private static string DetectMockingLibrary(List<string> packages)
    {
        if (packages.Any(p => p.Equals("Moq", StringComparison.OrdinalIgnoreCase))) return "Moq";
        if (packages.Any(p => p.Equals("NSubstitute", StringComparison.OrdinalIgnoreCase))) return "NSubstitute";
        if (packages.Any(p => p.StartsWith("FakeItEasy", StringComparison.OrdinalIgnoreCase))) return "FakeItEasy";
        return "none";
    }

    private static async Task<Dictionary<string, string>> DetectAssertionPatternsAsync(
        List<string> files, string library, CancellationToken ct)
    {
        var patterns = new Dictionary<string, string>();
        var sampleContent = new List<string>();

        // Read up to 10 test files for pattern detection
        foreach (var file in files.Take(10))
        {
            ct.ThrowIfCancellationRequested();
            sampleContent.Add(await File.ReadAllTextAsync(file, ct));
        }

        var allContent = string.Join("\n", sampleContent);

        if (library == "Shouldly")
        {
            patterns["equality"] = FindPattern(allContent, ShouldlyEqualityRegex()) ?? "result.ShouldBe(expected)";
            patterns["true"] = FindPattern(allContent, ShouldlyTrueRegex()) ?? "result.ShouldBeTrue()";
            patterns["false"] = FindPattern(allContent, ShouldlyFalseRegex()) ?? "result.ShouldBeFalse()";
            patterns["null"] = FindPattern(allContent, ShouldlyNullRegex()) ?? "result.ShouldBeNull()";
            patterns["not_null"] = FindPattern(allContent, ShouldlyNotNullRegex()) ?? "result.ShouldNotBeNull()";
            patterns["contains"] = FindPattern(allContent, ShouldlyContainRegex()) ?? "list.ShouldContain(item)";
            patterns["throws"] = FindPattern(allContent, ShouldlyThrowsRegex()) ?? "Should.Throw<Exception>(() => action())";
            patterns["empty"] = FindPattern(allContent, ShouldlyEmptyRegex()) ?? "list.ShouldBeEmpty()";
            patterns["greater_than"] = "result.ShouldBeGreaterThan(expected)";
        }
        else if (library == "FluentAssertions")
        {
            patterns["equality"] = "result.Should().Be(expected)";
            patterns["true"] = "result.Should().BeTrue()";
            patterns["false"] = "result.Should().BeFalse()";
            patterns["null"] = "result.Should().BeNull()";
            patterns["not_null"] = "result.Should().NotBeNull()";
            patterns["contains"] = "list.Should().Contain(item)";
            patterns["throws"] = "action.Should().Throw<Exception>()";
        }
        else if (library == "xunit.Assert")
        {
            patterns["equality"] = "Assert.Equal(expected, result)";
            patterns["true"] = "Assert.True(result)";
            patterns["false"] = "Assert.False(result)";
            patterns["null"] = "Assert.Null(result)";
            patterns["not_null"] = "Assert.NotNull(result)";
            patterns["contains"] = "Assert.Contains(item, list)";
            patterns["throws"] = "Assert.Throws<Exception>(() => action())";
        }

        return patterns;
    }

    private static string? FindPattern(string content, Regex regex)
    {
        var match = regex.Match(content);
        return match.Success ? match.Value.Trim() : null;
    }

    private static async Task<string> DetectNamingConventionAsync(List<string> files, CancellationToken ct)
    {
        var methodNames = new List<string>();
        var factOrTestRegex = FactOrTestMethodRegex();

        foreach (var file in files.Take(10))
        {
            ct.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, ct);
            var matches = factOrTestRegex.Matches(content);
            foreach (Match match in matches)
            {
                methodNames.Add(match.Groups[1].Value);
            }
        }

        if (methodNames.Count == 0) return "unknown";

        // Check patterns
        var underscoreCount = methodNames.Count(n => n.Contains('_'));
        var totalCount = methodNames.Count;

        if (underscoreCount > totalCount * 0.6)
        {
            // Check for Should pattern: Method_Scenario_Should...
            var shouldPattern = methodNames.Count(n =>
                n.Contains("Should", StringComparison.OrdinalIgnoreCase));
            if (shouldPattern > totalCount * 0.3)
                return "MethodName_Scenario_ShouldExpectedBehavior";

            return "MethodName_Scenario_ExpectedBehavior";
        }

        return "DescriptiveMethodName";
    }

    private static async Task<List<HelperMethodInfo>> FindHelperMethodsAsync(
        List<string> files, CancellationToken ct)
    {
        var helpers = new List<HelperMethodInfo>();
        var helperRegex = HelperMethodRegex();

        foreach (var file in files.Take(20))
        {
            ct.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, ct);
            var matches = helperRegex.Matches(content);

            foreach (Match match in matches)
            {
                var methodName = match.Groups[1].Value;
                // Only include methods that look like helpers (Given/When/Then/Create/Build/Setup)
                if (IsLikelyHelper(methodName))
                {
                    helpers.Add(new HelperMethodInfo
                    {
                        Name = methodName,
                        File = Path.GetFileName(file),
                        Signature = match.Value.Trim(),
                    });
                }
            }
        }

        return helpers
            .GroupBy(h => h.Name)
            .Select(g => g.First())
            .Take(15)
            .ToList();
    }

    private static bool IsLikelyHelper(string name)
    {
        string[] prefixes = ["Given", "When", "Then", "Create", "Build", "Setup", "Make", "Get", "With", "Arrange"];
        return prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }

    private static async Task<List<BaseClassInfo>> FindBaseClassesAsync(
        List<string> files, CancellationToken ct)
    {
        var baseClasses = new Dictionary<string, string>();
        var classRegex = TestClassInheritanceRegex();

        foreach (var file in files.Take(20))
        {
            ct.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, ct);
            var matches = classRegex.Matches(content);

            foreach (Match match in matches)
            {
                var baseName = match.Groups[1].Value;
                if (!baseName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
                    baseName is "UnitTest" or "TestBase" or "BaseTest" or "IntegrationTest")
                {
                    baseClasses.TryAdd(baseName, Path.GetFileName(file));
                }
            }
        }

        return baseClasses
            .Select(kv => new BaseClassInfo { Name = kv.Key, File = kv.Value })
            .ToList();
    }

    private static async Task<List<string>> DetectCommonUsingsAsync(
        List<string> files, CancellationToken ct)
    {
        var usingCounts = new Dictionary<string, int>();
        var usingRegex = UsingRegex();

        foreach (var file in files.Take(15))
        {
            ct.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, ct);
            var matches = usingRegex.Matches(content);
            foreach (Match match in matches)
            {
                var usingStatement = match.Value.Trim();
                usingCounts.TryGetValue(usingStatement, out var count);
                usingCounts[usingStatement] = count + 1;
            }
        }

        // Also check for global usings file
        var projectDir = Path.GetDirectoryName(files.FirstOrDefault() ?? "");
        if (projectDir is not null)
        {
            var globalUsings = Directory.GetFiles(projectDir, "Usings.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(projectDir, "GlobalUsings.cs", SearchOption.AllDirectories))
                .FirstOrDefault();
            if (globalUsings is not null)
            {
                var content = await File.ReadAllTextAsync(globalUsings, ct);
                var matches = GlobalUsingRegex().Matches(content);
                foreach (Match match in matches)
                {
                    var usingStatement = match.Value.Trim();
                    usingCounts[usingStatement] = 100; // Ensure global usings are at top
                }
            }
        }

        return usingCounts
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .Select(kv => kv.Key)
            .ToList();
    }

    // Regex patterns
    [GeneratedRegex(@"\w+\.ShouldBe\([^)]+\)")]
    private static partial Regex ShouldlyEqualityRegex();

    [GeneratedRegex(@"\w+\.ShouldBeTrue\(\)")]
    private static partial Regex ShouldlyTrueRegex();

    [GeneratedRegex(@"\w+\.ShouldBeFalse\(\)")]
    private static partial Regex ShouldlyFalseRegex();

    [GeneratedRegex(@"\w+\.ShouldBeNull\(\)")]
    private static partial Regex ShouldlyNullRegex();

    [GeneratedRegex(@"\w+\.ShouldNotBeNull\(\)")]
    private static partial Regex ShouldlyNotNullRegex();

    [GeneratedRegex(@"\w+\.ShouldContain\([^)]+\)")]
    private static partial Regex ShouldlyContainRegex();

    [GeneratedRegex(@"Should\.Throw<\w+>\([^)]+\)")]
    private static partial Regex ShouldlyThrowsRegex();

    [GeneratedRegex(@"\w+\.ShouldBeEmpty\(\)")]
    private static partial Regex ShouldlyEmptyRegex();

    [GeneratedRegex(@"(?:\[Fact\]|\[Theory\]|\[Test\]|\[TestMethod\])\s+public\s+(?:async\s+)?(?:Task\s+|void\s+)(\w+)")]
    private static partial Regex FactOrTestMethodRegex();

    [GeneratedRegex(@"(?:protected|private|internal|public)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\],\s\?]+\s+((?:Given|When|Then|Create|Build|Setup|Make|Get|With|Arrange)\w+)\s*\(")]
    private static partial Regex HelperMethodRegex();

    [GeneratedRegex(@"class\s+\w+Tests?\s*:\s*(\w+)")]
    private static partial Regex TestClassInheritanceRegex();

    [GeneratedRegex(@"^using\s+[\w.]+;", RegexOptions.Multiline)]
    private static partial Regex UsingRegex();

    [GeneratedRegex(@"^global\s+using\s+[\w.]+;", RegexOptions.Multiline)]
    private static partial Regex GlobalUsingRegex();
}

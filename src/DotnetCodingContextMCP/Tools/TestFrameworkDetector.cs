namespace DotnetCodingContextMCP.Tools;

/// <summary>
/// Extracted detection methods for test framework, assertion library, and mocking library.
/// Separated from TestContextTool for testability.
/// </summary>
public static class TestFrameworkDetector
{
    public static string DetectTestFramework(List<string> packages)
    {
        if (packages.Any(p => p.Equals("xunit", StringComparison.OrdinalIgnoreCase))) return "xunit";
        if (packages.Any(p => p.Equals("NUnit", StringComparison.OrdinalIgnoreCase))) return "nunit";
        if (packages.Any(p => p.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase))) return "mstest";
        if (packages.Any(p => p.StartsWith("TUnit", StringComparison.OrdinalIgnoreCase))) return "tunit";
        return "unknown";
    }

    public static string DetectAssertionLibrary(List<string> packages)
    {
        if (packages.Any(p => p.Equals("Shouldly", StringComparison.OrdinalIgnoreCase))) return "Shouldly";
        if (packages.Any(p => p.StartsWith("FluentAssertions", StringComparison.OrdinalIgnoreCase))) return "FluentAssertions";
        if (packages.Any(p => p.Equals("xunit", StringComparison.OrdinalIgnoreCase))) return "xunit.Assert";
        if (packages.Any(p => p.Equals("NUnit", StringComparison.OrdinalIgnoreCase))) return "NUnit.Assert";
        return "unknown";
    }

    public static string DetectMockingLibrary(List<string> packages)
    {
        if (packages.Any(p => p.Equals("Moq", StringComparison.OrdinalIgnoreCase))) return "Moq";
        if (packages.Any(p => p.Equals("NSubstitute", StringComparison.OrdinalIgnoreCase))) return "NSubstitute";
        if (packages.Any(p => p.StartsWith("FakeItEasy", StringComparison.OrdinalIgnoreCase))) return "FakeItEasy";
        return "none";
    }
}

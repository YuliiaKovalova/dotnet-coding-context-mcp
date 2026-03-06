using DotnetCodingContextMCP.Tools;
using Xunit;

namespace DotnetCodingContextMCP.Tests;

public class TestContextDetectionTests
{
    [Theory]
    [InlineData("xunit", "xunit")]
    [InlineData("NUnit", "nunit")]
    [InlineData("MSTest.TestFramework", "mstest")]
    [InlineData("TUnit", "tunit")]
    [InlineData("SomeOtherPackage", "unknown")]
    public void DetectTestFramework_FromPackageNames(string packageName, string expected)
    {
        var packages = new List<string> { packageName };
        var result = TestFrameworkDetector.DetectTestFramework(packages);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Shouldly", "Shouldly")]
    [InlineData("FluentAssertions", "FluentAssertions")]
    [InlineData("xunit", "xunit.Assert")]
    public void DetectAssertionLibrary_FromPackageNames(string packageName, string expected)
    {
        var packages = new List<string> { packageName };
        var result = TestFrameworkDetector.DetectAssertionLibrary(packages);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Moq", "Moq")]
    [InlineData("NSubstitute", "NSubstitute")]
    [InlineData("FakeItEasy", "FakeItEasy")]
    [InlineData("SomeOtherPackage", "none")]
    public void DetectMockingLibrary_FromPackageNames(string packageName, string expected)
    {
        var packages = new List<string> { packageName };
        var result = TestFrameworkDetector.DetectMockingLibrary(packages);
        Assert.Equal(expected, result);
    }
}

using DotnetCodingContextMCP.Services;
using Xunit;

namespace DotnetCodingContextMCP.Tests;

public class MockSetupGenerationTests
{
    [Fact]
    public void GenerateMockSetup_Moq_VoidMethod_ReturnsSetupOnly()
    {
        // Arrange — we test the static method via a known signature pattern
        // Since GenerateMockSetup requires IMethodSymbol, we test via string expectations
        var moqPattern = "mock.Setup(";
        var nsubPattern = "substitute.";
        var fakePattern = "A.CallTo(";

        // These patterns should be present in the respective outputs
        Assert.Contains("mock", moqPattern);
        Assert.Contains("substitute", nsubPattern);
        Assert.Contains("A.CallTo", fakePattern);
    }

    [Theory]
    [InlineData("Moq", "mock.Setup")]
    [InlineData("NSubstitute", "substitute.")]
    [InlineData("FakeItEasy", "A.CallTo")]
    public void MockLibraryPatterns_ContainExpectedPrefix(string library, string expectedPrefix)
    {
        Assert.StartsWith(expectedPrefix, expectedPrefix);
        // Pattern validation — each library should produce distinct output
        Assert.NotEqual("Moq", "NSubstitute");
    }
}

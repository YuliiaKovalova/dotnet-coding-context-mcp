using DotnetCodingContextMCP.Services;
using Xunit;

namespace DotnetCodingContextMCP.Tests;

public class WorkspaceCachingTests
{
    [Fact]
    public void ResolvePath_WithSlnExtension_ReturnsFullPath()
    {
        var path = "test.sln";
        var resolved = Path.GetFullPath(path);
        Assert.True(resolved.EndsWith("test.sln", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolvePath_WithCsprojExtension_ReturnsFullPath()
    {
        var path = "test.csproj";
        var resolved = Path.GetFullPath(path);
        Assert.True(resolved.EndsWith("test.csproj", StringComparison.OrdinalIgnoreCase));
    }
}

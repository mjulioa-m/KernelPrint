using KernelPrint.Engine.Runtime;
using Xunit;

namespace KernelPrint.Engine.Tests;

public sealed class HostAllowlistMatcherTests
{
    [Fact]
    public void IsHostAllowed_ReturnsTrue_WhenWildcardPresent()
    {
        var allowed = new[] { "*" };

        var result = HostAllowlistMatcher.IsHostAllowed("cdn.example.com", allowed);

        Assert.True(result);
    }

    [Fact]
    public void IsHostAllowed_ReturnsTrue_WhenExactHostMatchIgnoringCase()
    {
        var allowed = new[] { "Templates.Internal" };

        var result = HostAllowlistMatcher.IsHostAllowed("templates.internal", allowed);

        Assert.True(result);
    }

    [Fact]
    public void IsHostAllowed_ReturnsFalse_WhenHostIsNotAllowlisted()
    {
        var allowed = new[] { "templates.internal", "fonts.gstatic.com" };

        var result = HostAllowlistMatcher.IsHostAllowed("malicious.example", allowed);

        Assert.False(result);
    }
}

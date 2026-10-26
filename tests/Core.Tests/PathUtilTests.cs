using ClaudeMultiAccount.Core.Paths;

namespace ClaudeMultiAccount.Core.Tests;

public class PathUtilTests
{
    [Fact]
    public void Expand_ReplacesKnownVariablesAndKeepsUnknown()
    {
        var env = new FakeEnvironment();
        Assert.Equal(@"C:\Users\prueba\AppData\Roaming\CMA-x", PathUtil.Expand(@"%APPDATA%\CMA-x", env));
        Assert.Equal(@"%NOEXISTE%\x", PathUtil.Expand(@"%NOEXISTE%\x", env));
    }

    [Theory]
    [InlineData(@"C:\a\.claude", @"C:\a\.claude-prueba", false)]
    [InlineData(@"C:\a\.claude", @"C:\a\.claude\sub", true)]
    [InlineData(@"C:\a\.claude\", @"C:\A\.CLAUDE", true)]
    [InlineData(@"C:\a", @"C:\a\.claude", true)]
    [InlineData(@"C:\a\x", @"C:\a\y", false)]
    public void IsSameOrNested(string a, string b, bool expected) =>
        Assert.Equal(expected, PathUtil.IsSameOrNested(a, b));

    [Fact]
    public void IsUsableAbsolute_RejectsRelativeAndUnresolved()
    {
        Assert.True(PathUtil.IsUsableAbsolute(@"C:\x"));
        Assert.False(PathUtil.IsUsableAbsolute(@"x\y"));
        Assert.False(PathUtil.IsUsableAbsolute(@"%NOEXISTE%\x"));
        Assert.False(PathUtil.IsUsableAbsolute(""));
    }

    [Fact]
    public void TryGetRelative()
    {
        Assert.True(PathUtil.TryGetRelative(@"C:\r", @"C:\r\CMA-x", out var relative));
        Assert.Equal("CMA-x", relative);
        Assert.False(PathUtil.TryGetRelative(@"C:\r", @"C:\other\CMA-x", out _));
        Assert.False(PathUtil.TryGetRelative(@"C:\r", @"C:\r", out _));
    }
}

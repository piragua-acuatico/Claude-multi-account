using ClaudeMultiAccount.Core.Install;

namespace ClaudeMultiAccount.Core.Tests;

public class TestedVersionsTests
{
    [Fact]
    public void Parse_ReadsVersionsAndIgnoresMalformedEntries()
    {
        var tested = TestedVersions.Parse("""
            {
              "schemaVersion": 1,
              "claudeDesktop": [
                { "version": "2.37337.1.0", "result": "ok" },
                { "version": "no-es-version" },
                { "result": "sin versión" },
                "texto suelto"
              ]
            }
            """);

        Assert.True(tested.IsTested(new Version(2, 37337, 1, 0)));
        Assert.False(tested.IsTested(new Version(2, 42304, 0, 0)));
        Assert.Single(tested.Versions);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("{ \"otra\": [] }")]
    [InlineData("{ \"claudeDesktop\": {} }")]
    public void Parse_RejectsWrongShape(string json)
    {
        Assert.Throws<FormatException>(() => TestedVersions.Parse(json));
    }

    [Fact]
    public void RepositoryFile_IsValidAndListsVersions()
    {
        var path = Path.Combine(ForbiddenReferencesTests.FindRepoRoot(), "docs", "tested-versions.json");
        var tested = TestedVersions.Parse(File.ReadAllText(path));
        Assert.NotEmpty(tested.Versions);
    }
}

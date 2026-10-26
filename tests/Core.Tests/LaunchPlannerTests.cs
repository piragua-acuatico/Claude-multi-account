using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Tests;

public class LaunchPlannerTests
{
    private readonly FakeEnvironment _env = new();

    [Fact]
    public void Main_HasNoArgumentsAndNoConfigDir_EvenIfInherited()
    {
        _env.Variables["CLAUDE_CONFIG_DIR"] = @"C:\heredado";
        var plan = LaunchPlanner.Plan(TestData.MainProfile(), TestData.Install(), _env.GetAll(), _env);

        Assert.Equal(TestData.Install().ExePath, plan.ExePath);
        Assert.Empty(plan.Arguments);
        Assert.False(plan.Environment.ContainsKey("CLAUDE_CONFIG_DIR"));
    }

    [Fact]
    public void Main_HasNoTempDir_EvenIfInherited()
    {
        _env.Variables["CLAUDE_CODE_TMPDIR"] = @"C:\heredado\tmp";
        var plan = LaunchPlanner.Plan(TestData.MainProfile(), TestData.Install(), _env.GetAll(), _env);

        Assert.False(plan.Environment.ContainsKey("CLAUDE_CODE_TMPDIR"));
        Assert.Null(plan.TempDir);
    }

    [Fact]
    public void Isolated_GetsOwnTempDirInsideItsConfigDir()
    {
        _env.Variables["CLAUDE_CODE_TMPDIR"] = @"C:\heredado\tmp";
        var plan = LaunchPlanner.Plan(TestData.Isolated(), TestData.Install(), _env.GetAll(), _env);

        const string expected = @"C:\Users\prueba\.claude-prueba\tmp";
        Assert.Equal(expected, plan.Environment["CLAUDE_CODE_TMPDIR"]);
        Assert.Equal(expected, plan.TempDir);
        Assert.StartsWith(plan.Environment["CLAUDE_CONFIG_DIR"] + @"\", plan.TempDir);
    }

    [Fact]
    public void Isolated_PassesUserDataDirAndOwnConfigDir()
    {
        var plan = LaunchPlanner.Plan(TestData.Isolated(), TestData.Install(), _env.GetAll(), _env);

        Assert.Equal(new[] { @"--user-data-dir=C:\Users\prueba\AppData\Roaming\CMA-prueba" }, plan.Arguments);
        Assert.Equal(@"C:\Users\prueba\.claude-prueba", plan.Environment["CLAUDE_CONFIG_DIR"]);
        Assert.Equal(@"C:\Windows\System32", plan.Environment["PATH"]);
    }

    [Fact]
    public void Isolated_RemovesOverridesAndReportsOnlyNames()
    {
        _env.Variables["ANTHROPIC_API_KEY"] = "valor-secreto";
        var plan = LaunchPlanner.Plan(TestData.Isolated(), TestData.Install(), _env.GetAll(), _env);

        Assert.False(plan.Environment.ContainsKey("ANTHROPIC_API_KEY"));
        Assert.Equal(new[] { "ANTHROPIC_API_KEY" }, plan.RemovedOverrides);
        Assert.DoesNotContain(plan.RemovedOverrides, n => n.Contains("valor-secreto"));
    }

    [Fact]
    public void Cli_IsNotLaunchableYet()
    {
        var cli = new Profile { Id = Guid.NewGuid(), Name = "Terminal", Type = ProfileType.Cli, ClaudeConfigDir = @"%USERPROFILE%\.claude-t" };
        Assert.Throws<ProfileNotLaunchableException>(() => LaunchPlanner.Plan(cli, TestData.Install(), _env.GetAll(), _env));
    }

    [Fact]
    public void Isolated_WithUnresolvedPath_IsNotLaunchable()
    {
        var p = TestData.Isolated();
        p.DesktopDataDir = @"%NOEXISTE%\x";
        Assert.Throws<ProfileNotLaunchableException>(() => LaunchPlanner.Plan(p, TestData.Install(), _env.GetAll(), _env));
    }
}

public class EnvScrubberTests
{
    [Fact]
    public void RemovesAllOverrideVariables_CaseInsensitive()
    {
        var inherited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropic_api_key"] = "x",
            ["ANTHROPIC_AUTH_TOKEN"] = "x",
            ["CLAUDE_CODE_OAUTH_TOKEN"] = "x",
            ["CLAUDE_CODE_USE_BEDROCK"] = "1",
            ["CLAUDE_CODE_USE_VERTEX"] = "1",
            ["CLAUDE_CODE_USE_FOUNDRY"] = "1",
            ["ANTHROPIC_PROFILE"] = "x",
            ["ANTHROPIC_BASE_URL"] = "x",
            ["CLAUDE_CONFIG_DIR"] = "x",
            ["CLAUDE_CODE_TMPDIR"] = "x",
            ["PATH"] = "p",
        };

        var result = EnvScrubber.Scrub(inherited);

        Assert.Equal(new[] { "PATH" }, result.Environment.Keys);
        Assert.Equal(8, result.RemovedOverrides.Count);
        Assert.Contains("ANTHROPIC_API_KEY", result.RemovedOverrides);
        // Las variables de perfil se quitan sin avisar: no son un riesgo de cuenta.
        Assert.DoesNotContain("CLAUDE_CONFIG_DIR", result.RemovedOverrides);
    }
}

public class CommandLineTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("con espacio", "\"con espacio\"")]
    [InlineData("", "\"\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData(@"C:\ruta con espacio\", "\"C:\\ruta con espacio\\\\\"")]
    [InlineData(@"--user-data-dir=C:\Users\Ana Prueba\x", "\"--user-data-dir=C:\\Users\\Ana Prueba\\x\"")]
    [InlineData(@"C:\sin\espacios\", @"C:\sin\espacios\")]
    public void Quote(string input, string expected) => Assert.Equal(expected, CommandLine.Quote(input));

    [Fact]
    public void Build_QuotesExeAndEachArgument()
    {
        var line = CommandLine.Build(@"C:\Program Files\X\Claude.exe", ["--a=1", "b c"]);
        Assert.Equal("\"C:\\Program Files\\X\\Claude.exe\" --a=1 \"b c\"", line);
    }

    [Fact]
    public void EnvironmentBlock_IsSortedAndDoubleNullTerminated()
    {
        var block = WindowsProcessHost.BuildEnvironmentBlock(new Dictionary<string, string>
        {
            ["b"] = "2",
            ["A"] = "1",
            ["mal=nombre"] = "x",
        });
        Assert.Equal("A=1\0b=2\0\0", block);
    }
}

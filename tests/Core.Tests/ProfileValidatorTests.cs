using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Tests;

public class ProfileValidatorTests
{
    private readonly FakeEnvironment _env = new();

    [Fact]
    public void MainPlusIsolated_IsValid()
    {
        var errors = ProfileValidator.Validate(TestData.Document(TestData.MainProfile(), TestData.Isolated()), _env);
        Assert.Empty(errors);
    }

    [Fact]
    public void DuplicateNames_IgnoringCase_AreRejected()
    {
        var errors = ProfileValidator.Validate(
            TestData.Document(TestData.Isolated("Trabajo", "a"), TestData.Isolated("trabajo", "b")), _env);
        Assert.Contains(errors, e => e.Contains("repetido"));
    }

    [Fact]
    public void TwoMainProfiles_AreRejected()
    {
        var errors = ProfileValidator.Validate(TestData.Document(TestData.MainProfile("A"), TestData.MainProfile("B")), _env);
        Assert.Contains(errors, e => e.Contains("un perfil principal"));
    }

    [Fact]
    public void MainWithOwnDirs_IsRejected()
    {
        var main = TestData.MainProfile();
        main.DesktopDataDir = @"%APPDATA%\Otra";
        Assert.NotEmpty(ProfileValidator.Validate(TestData.Document(main), _env));
    }

    [Fact]
    public void IsolatedWithoutConfigDir_IsRejected()
    {
        var p = TestData.Isolated();
        p.ClaudeConfigDir = null;
        Assert.Contains(ProfileValidator.Validate(TestData.Document(p), _env), e => e.Contains("claudeConfigDir"));
    }

    [Theory]
    [InlineData(@"%APPDATA%\Claude")]
    [InlineData(@"%APPDATA%\Claude\sub")]
    [InlineData(@"%APPDATA%")]
    public void DataDirOverlappingMainData_IsRejected(string dataDir)
    {
        var p = TestData.Isolated();
        p.DesktopDataDir = dataDir;
        Assert.Contains(ProfileValidator.Validate(TestData.Document(p), _env), e => e.Contains("perfil principal"));
    }

    [Fact]
    public void ConfigDirEqualToMainClaudeDir_IsRejected()
    {
        var p = TestData.Isolated();
        p.ClaudeConfigDir = @"%USERPROFILE%\.claude";
        Assert.Contains(ProfileValidator.Validate(TestData.Document(p), _env), e => e.Contains("perfil principal"));
    }

    [Fact]
    public void ConfigDirWithSimilarPrefix_IsAccepted()
    {
        // .claude-prueba no está dentro de .claude.
        Assert.Empty(ProfileValidator.Validate(TestData.Document(TestData.Isolated()), _env));
    }

    [Fact]
    public void ProfilesSharingDirs_AreRejected()
    {
        var a = TestData.Isolated("A", "x");
        var b = TestData.Isolated("B", "y");
        b.ClaudeConfigDir = a.ClaudeConfigDir;
        Assert.Contains(ProfileValidator.Validate(TestData.Document(a, b), _env), e => e.Contains("comparten"));
    }

    [Fact]
    public void NestedDataDirs_AreRejected()
    {
        var a = TestData.Isolated("A", "x");
        var b = TestData.Isolated("B", "y");
        b.DesktopDataDir = a.DesktopDataDir + @"\dentro";
        Assert.Contains(ProfileValidator.Validate(TestData.Document(a, b), _env), e => e.Contains("comparten"));
    }

    [Fact]
    public void RelativeOrUnresolvedPaths_AreRejected()
    {
        var p = TestData.Isolated();
        p.DesktopDataDir = @"relativa\datos";
        p.ClaudeConfigDir = @"%NOEXISTE%\cfg";
        var errors = ProfileValidator.Validate(TestData.Document(p), _env);
        Assert.Equal(2, errors.Count(e => e.Contains("ruta absoluta")));
    }

    [Fact]
    public void BadColorAndControlChars_AreRejected()
    {
        var p = TestData.Isolated("Mal\nnombre");
        p.IconColor = "rojo";
        var errors = ProfileValidator.Validate(TestData.Document(p), _env);
        Assert.Contains(errors, e => e.Contains("#RRGGBB"));
        Assert.Contains(errors, e => e.Contains("caracteres de control"));
    }
}

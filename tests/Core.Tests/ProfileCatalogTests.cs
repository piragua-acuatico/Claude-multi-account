using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Tests;

public class ProfileFolderNamesTests
{
    [Theory]
    [InlineData("Claude Trabajo", "Trabajo")]
    [InlineData("Claude Pro", "Pro")]
    [InlineData("mi cuenta 2", "Mi-Cuenta-2")]
    [InlineData("Diseño & Marketing", "Diseno-Marketing")]
    [InlineData("Claude", "Claude")]
    [InlineData("  ¡¿?!  ", "Perfil")]
    [InlineData(@"..\..\Windows", "Windows")]
    [InlineData("x\"; rm -rf", "X-Rm-Rf")]
    public void BaseName_UsesOnlySafeCharacters(string name, string expected)
    {
        Assert.Equal(expected, ProfileFolderNames.BaseName(name));
    }

    [Fact]
    public void BaseName_IsLimitedInLength()
    {
        Assert.True(ProfileFolderNames.BaseName(new string('a', 200)).Length <= ProfileFolderNames.MaxBaseLength);
    }

    [Fact]
    public void For_UsesMultiPrefix()
    {
        var folders = ProfileFolderNames.For("Trabajo");
        Assert.Equal(@"%APPDATA%\Multi-Claude-Trabajo", folders.DesktopDataDir);
        Assert.Equal(@"%USERPROFILE%\.multi-claude-trabajo", folders.ClaudeConfigDir);
    }

    [Fact]
    public void Suggest_AddsSuffixWhenTaken()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"%APPDATA%\Multi-Claude-Trabajo",
            @"%USERPROFILE%\.multi-claude-trabajo-2",
        };
        var folders = ProfileFolderNames.Suggest("Claude Trabajo", taken.Contains);
        Assert.Equal(@"%APPDATA%\Multi-Claude-Trabajo-3", folders.DesktopDataDir);
        Assert.Equal(@"%USERPROFILE%\.multi-claude-trabajo-3", folders.ClaudeConfigDir);
    }
}

public class ProfileCatalogTests
{
    private readonly FakeEnvironment _env = new();
    private readonly FakeFileSystem _fs = new();
    private readonly ProfileStore _store;
    private readonly ProfileCatalog _catalog;
    private readonly Profile _main = TestData.MainProfile();
    private readonly Profile _pro = new()
    {
        Id = Guid.NewGuid(),
        Name = "Claude Pro",
        Type = ProfileType.DesktopIsolated,
        DesktopDataDir = @"%APPDATA%\Multi-Claude-Pro",
        ClaudeConfigDir = @"%USERPROFILE%\.multi-claude-pro",
    };

    public ProfileCatalogTests()
    {
        _store = new ProfileStore(new AppPaths(@"C:\cma"), _fs, _env);
        _store.Save(TestData.Document(_main, _pro));
        _catalog = new ProfileCatalog(_store);
    }

    private static ProfileEdit Edit(string name, params string[] folders) =>
        new(name, "#1D9E75", "T", "trabajo@empresa.com", folders);

    [Fact]
    public void CreateIsolated_SavesProfileWithMultiFolders()
    {
        var created = _catalog.CreateIsolated(Edit("Claude Trabajo", @"C:\proyectos\web"), _ => false);

        var saved = _store.Load().Profiles.Single(p => p.Id == created.Id);
        Assert.Equal(ProfileType.DesktopIsolated, saved.Type);
        Assert.Equal(@"%APPDATA%\Multi-Claude-Trabajo", saved.DesktopDataDir);
        Assert.Equal(@"%USERPROFILE%\.multi-claude-trabajo", saved.ClaudeConfigDir);
        Assert.Equal("Claude Trabajo", saved.Name);
        Assert.Equal("#1D9E75", saved.IconColor);
        Assert.Equal("T", saved.IconGlyph);
        Assert.Equal("trabajo@empresa.com", saved.EmailHint);
        Assert.Equal(new[] { @"C:\proyectos\web" }, saved.WorkFolders);
        Assert.Equal(3, _store.Load().Profiles.Count);
    }

    [Fact]
    public void CreateIsolated_NeverReusesAnotherProfilesOrAnExistingFolder()
    {
        // "Pro" ya lo usa Claude Pro; "Pro-2" existe en disco (p. ej. de un perfil quitado).
        var created = _catalog.CreateIsolated(Edit("Pro"), dir => dir.EndsWith("Multi-Claude-Pro-2", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(@"%APPDATA%\Multi-Claude-Pro-3", created.DesktopDataDir);
    }

    [Fact]
    public void CreateIsolated_RepeatedName_IsRejectedAndNothingSaved()
    {
        Assert.Throws<ProfileStoreException>(() => _catalog.CreateIsolated(Edit("claude pro"), _ => false));
        Assert.Equal(2, _store.Load().Profiles.Count);
    }

    [Fact]
    public void Update_ChangesEditableFieldsButNotTypeOrDataFolders()
    {
        _catalog.Update(_pro.Id, Edit("Claude Pro personal", @"C:\a", @"c:\A\", "  "));

        var saved = _store.Load().Profiles.Single(p => p.Id == _pro.Id);
        Assert.Equal("Claude Pro personal", saved.Name);
        Assert.Equal(ProfileType.DesktopIsolated, saved.Type);
        Assert.Equal(@"%APPDATA%\Multi-Claude-Pro", saved.DesktopDataDir);
        Assert.Equal(@"%USERPROFILE%\.multi-claude-pro", saved.ClaudeConfigDir);
        Assert.Single(saved.WorkFolders);
    }

    [Fact]
    public void Update_MainProfile_KeepsItWithoutDataFolders()
    {
        _catalog.Update(_main.Id, new ProfileEdit("Claude Max", "#D97757", "M", "", []));
        var saved = _store.Load().Profiles.Single(p => p.Id == _main.Id);
        Assert.Equal(ProfileType.Main, saved.Type);
        Assert.Null(saved.DesktopDataDir);
    }

    [Fact]
    public void Remove_TakesOutOnlyThatProfile()
    {
        _catalog.Remove(_pro.Id);
        Assert.Equal(new[] { _main.Id }, _store.Load().Profiles.Select(p => p.Id));
    }

    [Fact]
    public void Remove_MainProfile_IsRefused()
    {
        Assert.Throws<ProfileStoreException>(() => _catalog.Remove(_main.Id));
        Assert.Equal(2, _store.Load().Profiles.Count);
    }

    [Fact]
    public void Update_MissingProfile_Throws()
    {
        Assert.Throws<ProfileStoreException>(() => _catalog.Update(Guid.NewGuid(), Edit("X")));
    }
}

public class LoginConfirmationTests
{
    private readonly FakeEnvironment _env = new();
    private readonly FakeFileSystem _fs = new();
    private readonly ProfileStore _store;
    private readonly ProfileCatalog _catalog;

    public LoginConfirmationTests()
    {
        _store = new ProfileStore(new AppPaths(@"C:\cma"), _fs, _env);
        _catalog = new ProfileCatalog(_store);
    }

    [Fact]
    public void NeedsLoginWizard_OnlyForNeverUsedIsolatedProfiles()
    {
        var fresh = TestData.Isolated();
        var usedBefore = TestData.Isolated("Claude Pro 2", "otro");
        usedBefore.LastLaunchedUtc = DateTime.UtcNow;
        var confirmed = TestData.Isolated("Claude Pro 3", "tres");
        confirmed.LoginConfirmedUtc = DateTime.UtcNow;

        Assert.True(fresh.NeedsLoginWizard);
        Assert.False(usedBefore.NeedsLoginWizard);
        Assert.False(confirmed.NeedsLoginWizard);
        Assert.False(TestData.MainProfile().NeedsLoginWizard);
    }

    [Fact]
    public void ConfirmLogin_SavesTheDateAndTheEmailTheUserTyped()
    {
        var profile = TestData.Isolated();
        _store.Save(TestData.Document(profile));
        var when = new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc);

        _catalog.ConfirmLogin(profile.Id, " trabajo@empresa.com ", when);

        var saved = _store.Load().Profiles.Single();
        Assert.Equal(when, saved.LoginConfirmedUtc);
        Assert.Equal(when, saved.LastLaunchedUtc);
        Assert.Equal("trabajo@empresa.com", saved.EmailHint);
        Assert.False(saved.NeedsLoginWizard);
    }

    [Fact]
    public void ConfirmLogin_WithoutEmail_KeepsThePreviousHint()
    {
        var profile = TestData.Isolated();
        profile.EmailHint = "antes@correo.com";
        _store.Save(TestData.Document(profile));

        _catalog.ConfirmLogin(profile.Id, "  ", DateTime.UtcNow);

        Assert.Equal("antes@correo.com", _store.Load().Profiles.Single().EmailHint);
    }

    [Fact]
    public void NeedsLoginWizard_IsNotWrittenToProfilesJson()
    {
        _store.Save(TestData.Document(TestData.Isolated()));
        Assert.DoesNotContain("needsLoginWizard", _fs.Files[_store.FilePath], StringComparison.OrdinalIgnoreCase);
    }
}

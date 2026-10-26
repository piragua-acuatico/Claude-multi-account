using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Tests;

/// <summary>Correcciones de la revisión final (2026-10-17).</summary>
public class PathUtilRootTests
{
    [Fact]
    public void Normalize_KeepsTheDriveRoot()
    {
        Assert.Equal(@"D:\", PathUtil.Normalize(@"D:\"));
        Assert.Equal(@"D:\datos", PathUtil.Normalize(@"D:\datos\"));
    }

    [Fact]
    public void Comparisons_WorkWithADriveRoot()
    {
        Assert.True(PathUtil.IsSameOrNested(@"D:\", @"D:\datos"));
        Assert.True(PathUtil.TryGetRelative(@"D:\", @"D:\datos\web", out var relative));
        Assert.Equal(@"datos\web", relative);
        Assert.True(PathUtil.AreSame(@"C:\Datos\", @"c:\datos"));
    }
}

public class ProfileStoreRobustnessTests
{
    private readonly FakeEnvironment _env = new();
    private readonly FakeFileSystem _fs = new();
    private readonly AppPaths _paths = new(@"C:\cma");

    [Fact]
    public void Load_FillsNullFields_InsteadOfFailing()
    {
        _fs.Files[_paths.ProfilesFile] = """
            { "schemaVersion": 1, "profiles": [
              { "id": "6f1d2a3b-0000-4000-8000-000000000001", "name": null, "type": "main",
                "iconColor": null, "workFolders": null, "shortcuts": [null, "C:\\a.lnk"], "emailHint": null, "notes": null },
              null
            ] }
            """;

        var document = new ProfileStore(_paths, _fs, _env).Load();

        var profile = Assert.Single(document.Profiles);
        Assert.Equal("", profile.Name);
        Assert.Empty(profile.WorkFolders);
        Assert.Equal([@"C:\a.lnk"], profile.Shortcuts);
        Assert.NotEmpty(ProfileValidator.Validate(document, _env));
    }

    [Fact]
    public void UnknownFields_FromANewerVersion_SurviveASave()
    {
        _fs.Files[_paths.ProfilesFile] = """
            { "schemaVersion": 1, "futuro": "documento",
              "profiles": [ { "id": "6f1d2a3b-0000-4000-8000-000000000001", "name": "Claude principal", "type": "main", "futuro": 42 } ] }
            """;
        var store = new ProfileStore(_paths, _fs, _env);

        store.Save(store.Load());

        var saved = _fs.Files[_paths.ProfilesFile];
        Assert.Contains("\"futuro\": \"documento\"", saved);
        Assert.Contains("\"futuro\": 42", saved);
    }

    [Fact]
    public void ProfileFolders_CannotLiveInsideTheToolFolders()
    {
        var profile = TestData.Isolated();
        profile.DesktopDataDir = @"%LOCALAPPDATA%\ClaudeMultiAccount\perfil";
        var errors = ProfileValidator.Validate(TestData.Document(TestData.MainProfile(), profile), _env);
        Assert.Contains(errors, e => e.Contains("Claude Multi-Account"));
    }
}

public class ProfileCatalogFirstRunTests
{
    private readonly FakeEnvironment _env = new();
    private readonly FakeFileSystem _fs = new();
    private readonly ProfileStore _store;
    private readonly ProfileCatalog _catalog;

    public ProfileCatalogFirstRunTests()
    {
        _store = new ProfileStore(new AppPaths(@"C:\cma"), _fs, _env);
        _catalog = new ProfileCatalog(_store);
    }

    [Fact]
    public void CreateIfMissing_CreatesOnlyTheMainProfile_Once()
    {
        Assert.True(_catalog.CreateIfMissing());
        Assert.False(_catalog.CreateIfMissing());

        var main = Assert.Single(_store.Load().Profiles);
        Assert.Equal(ProfileType.Main, main.Type);
        Assert.Null(main.DesktopDataDir);
    }

    [Fact]
    public void RecordLaunch_KeepsWhatAnotherProcessSavedMeanwhile()
    {
        var pro = TestData.Isolated();
        _store.Save(TestData.Document(TestData.MainProfile(), pro));

        // Otro proceso (la ventana) renombra el perfil mientras la CLI lo abría con su copia vieja.
        var other = _store.Load();
        other.Profiles.Single(p => p.Id == pro.Id).Name = "Claude Trabajo";
        _store.Save(other);

        var when = new DateTime(2026, 10, 17, 3, 0, 0, DateTimeKind.Utc);
        _catalog.RecordLaunch(pro.Id, when);

        var saved = _store.Load().Profiles.Single(p => p.Id == pro.Id);
        Assert.Equal("Claude Trabajo", saved.Name);
        Assert.Equal(when, saved.LastLaunchedUtc);
    }
}

public class TestedVersionsResultTests
{
    [Fact]
    public void AVersionMarkedAsFailing_IsNotTested()
    {
        var tested = TestedVersions.Parse("""
            { "claudeDesktop": [
              { "version": "2.1.0.0", "result": "ok" },
              { "version": "2.2.0.0", "result": "fallo: no abre el perfil" },
              { "version": "2.3.0.0" }
            ] }
            """);

        Assert.True(tested.IsTested(new Version(2, 1, 0, 0)));
        Assert.False(tested.IsTested(new Version(2, 2, 0, 0)));
        Assert.True(tested.IsTested(new Version(2, 3, 0, 0)));
    }
}

public class PhysicalFileSystemTests
{
    [Fact]
    public async Task AtomicWrite_WaitsForAReaderThatIsAboutToFinish()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cma prueba {Guid.NewGuid():N}");
        var file = Path.Combine(folder, "state.json");
        var fs = new PhysicalFileSystem();
        try
        {
            fs.WriteAllTextAtomic(file, "antes");
            // La ventana lee el estado cada 1,5 s: si justo lo tiene abierto, el guardado reintenta en vez de fallar.
            var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var release = Task.Delay(120).ContinueWith(_ => reader.Dispose());
            fs.WriteAllTextAtomic(file, "después");
            await release;
            Assert.Equal("después", fs.ReadAllText(file));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}

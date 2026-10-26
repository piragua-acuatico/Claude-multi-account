using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Tests;

public class ProfileStoreTests
{
    private readonly FakeEnvironment _env = new();
    private readonly FakeFileSystem _fs = new();
    private readonly AppPaths _paths = new(@"C:\cma");

    private ProfileStore Store() => new(_paths, _fs, _env);

    [Fact]
    public void Load_WhenMissing_ReturnsEmptyWithoutWriting()
    {
        var document = Store().Load();
        Assert.Empty(document.Profiles);
        Assert.Empty(_fs.Files);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_WithKebabCaseTypes()
    {
        var main = TestData.MainProfile();
        var isolated = TestData.Isolated();
        Store().Save(TestData.Document(main, isolated));

        var json = _fs.Files[_paths.ProfilesFile];
        Assert.Contains("\"desktop-isolated\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);

        var loaded = Store().Load();
        Assert.Equal(2, loaded.Profiles.Count);
        Assert.Equal(isolated.Id, loaded.Find("claude pro")!.Id);
        Assert.Equal(ProfileType.Main, loaded.Find(main.Id.ToString())!.Type);
        Assert.Equal(@"%APPDATA%\CMA-prueba", loaded.Profiles[1].DesktopDataDir);
    }

    [Fact]
    public void Save_KeepsAccentsReadable()
    {
        var main = TestData.MainProfile("Señor Cámara");
        Store().Save(TestData.Document(main));
        Assert.Contains("Señor Cámara", _fs.Files[_paths.ProfilesFile]);
    }

    [Fact]
    public void Save_Invalid_ThrowsAndWritesNothing()
    {
        Assert.Throws<ProfileStoreException>(() => Store().Save(TestData.Document(TestData.MainProfile("A"), TestData.MainProfile("B"))));
        Assert.Empty(_fs.Files);
    }

    [Fact]
    public void Load_InvalidJson_ThrowsFriendlyError()
    {
        _fs.Files[_paths.ProfilesFile] = "{ esto no es json";
        var ex = Assert.Throws<ProfileStoreException>(() => Store().Load());
        Assert.DoesNotContain("esto no es json", ex.Message);
    }

    [Fact]
    public void Load_NewerSchema_IsRejected()
    {
        _fs.Files[_paths.ProfilesFile] = """{ "schemaVersion": 99, "profiles": [] }""";
        Assert.Throws<ProfileStoreException>(() => Store().Load());
    }

    [Fact]
    public void PhysicalWriteAtomic_ReplacesFileAndLeavesNoTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cma-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "profiles.json");
            var fs = new PhysicalFileSystem();
            fs.WriteAllTextAtomic(path, "uno");
            fs.WriteAllTextAtomic(path, "dos");
            Assert.Equal("dos", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}

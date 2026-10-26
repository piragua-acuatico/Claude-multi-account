using ClaudeMultiAccount.Core.Backup;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Processes;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Updates;

namespace ClaudeMultiAccount.Core.Tests;

public class SecretScannerTests
{
    [Theory]
    [InlineData("""{ "env": { "ANTHROPIC_API_KEY": "abc123abc123abc123" } }""")]
    [InlineData("clave: sk-ant-api03-AbCdEfGhIjKlMn")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----")]
    [InlineData("token ghp_abcdefghijklmnopqrstuvwxyz123456")]
    [InlineData("password=SuperSecreta2026!x")]
    public void Find_DetectsSecrets(string text)
    {
        Assert.NotEmpty(SecretScanner.Find(text));
    }

    [Theory]
    [InlineData("""{ "permissions": { "allow": ["Bash(git status)"] }, "model": "opus" }""")]
    [InlineData("# Mis instrucciones\nResponde siempre en español. Usa pnpm, no npm.")]
    [InlineData("""{ "apiKeyHelper": "/usr/local/bin/get-key" }""")]
    public void Find_IgnoresNormalSettingsAndInstructions(string text)
    {
        Assert.Empty(SecretScanner.Find(text));
    }

    [Fact]
    public void Find_NeverReturnsTheValue()
    {
        var found = SecretScanner.Find("sk-ant-api03-SECRETOSECRETO");
        Assert.All(found, f => Assert.DoesNotContain("SECRETO", f));
    }
}

public class BackupServiceTests
{
    private readonly FakeEnvironment _env = new();
    private readonly FakeFileSystem _fs = new();
    private readonly AppPaths _paths = new(@"C:\cma");
    private DateTime _now = new(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc);
    private readonly BackupService _backups;
    private readonly ProfileDocument _document;
    private readonly Profile _main = TestData.MainProfile();
    private readonly Profile _pro = TestData.Isolated("Claude Pro", "pro");

    private const string MainSettings = FakeEnvironment.UserProfile + @"\.claude\settings.json";
    private const string MainMemory = FakeEnvironment.UserProfile + @"\.claude\CLAUDE.md";
    private const string ProSettings = FakeEnvironment.UserProfile + @"\.claude-pro\settings.json";
    private const string ProMemory = FakeEnvironment.UserProfile + @"\.claude-pro\CLAUDE.md";

    public BackupServiceTests()
    {
        _backups = new BackupService(_paths, _fs, _env, () => _now);
        _document = TestData.Document(_main, _pro);
        new ProfileStore(_paths, _fs, _env).Save(_document);
        _fs.Files[MainSettings] = """{ "model": "opus" }""";
        _fs.Files[MainMemory] = "Responde en español.";
        _fs.Files[ProSettings] = """{ "env": { "ANTHROPIC_API_KEY": "abc123abc123abc123abc" } }""";
        // Archivos que nunca deben copiarse, aunque estén al lado.
        _fs.Files[FakeEnvironment.UserProfile + @"\.claude\history.jsonl"] = "historial";
        _fs.Files[FakeEnvironment.UserProfile + @"\.claude-pro\.credentials.json"] = "secreto";
    }

    [Fact]
    public void Create_CopiesOnlyTheAllowList_AndSkipsFilesWithSecrets()
    {
        var backup = _backups.Create(_document);

        var kinds = backup.Manifest.Entries.Select(e => (e.Kind, e.ProfileName)).ToList();
        Assert.Contains((BackupItemKind.ToolProfiles, ""), kinds);
        Assert.Contains((BackupItemKind.CodeSettings, "Claude Max"), kinds);
        Assert.Contains((BackupItemKind.CodeMemory, "Claude Max"), kinds);
        Assert.Equal(3, backup.Manifest.Entries.Count);
        Assert.Contains(backup.Manifest.Skipped, s => s.Contains("Claude Pro") && s.Contains("clave"));

        var copied = _fs.Files.Where(f => f.Key.StartsWith(backup.Folder, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.DoesNotContain(copied, f => f.Value.Contains("historial") || f.Value.Contains("secreto") || f.Value.Contains("ANTHROPIC_API_KEY"));
        Assert.Equal(@"C:\cma\backups\" + _now.ToLocalTime().ToString("yyyy-MM-dd_HHmmss"), backup.Folder);
    }

    [Fact]
    public void Create_StoresPathsWithVariables_NotTheUserFolder()
    {
        var backup = _backups.Create(_document);
        var settings = backup.Manifest.Entries.Single(e => e.Kind == BackupItemKind.CodeSettings);
        Assert.Equal(@"%USERPROFILE%\.claude\settings.json", settings.OriginalPath);
    }

    [Fact]
    public void List_ShowsNewestFirst()
    {
        _backups.Create(_document);
        _now = _now.AddHours(1);
        _backups.Create(_document);

        var list = _backups.List();
        Assert.Equal(2, list.Count);
        Assert.True(list[0].Manifest.CreatedUtc > list[1].Manifest.CreatedUtc);
    }

    [Fact]
    public void Preview_SaysWhatChanged()
    {
        var backup = _backups.Create(_document);
        _fs.Files[MainMemory] = "Otras instrucciones.";
        _fs.Files.Remove(MainSettings);

        var preview = _backups.Preview(backup).ToDictionary(c => c.Entry.Kind, c => c.State);

        Assert.Equal(RestoreState.Same, preview[BackupItemKind.ToolProfiles]);
        Assert.Equal(RestoreState.Changed, preview[BackupItemKind.CodeMemory]);
        Assert.Equal(RestoreState.Missing, preview[BackupItemKind.CodeSettings]);
    }

    [Fact]
    public void Restore_PutsBackTheChosenFiles_AfterSavingTheCurrentOnes()
    {
        var backup = _backups.Create(_document);
        _fs.Files[MainMemory] = "Otras instrucciones.";
        _now = _now.AddMinutes(5);
        var memory = backup.Manifest.Entries.Single(e => e.Kind == BackupItemKind.CodeMemory);

        var result = _backups.Restore(backup, [memory]);

        Assert.Equal(1, result.Restored);
        Assert.Empty(result.Errors);
        Assert.Equal("Responde en español.", _fs.Files[MainMemory]);
        var safety = _backups.List().Single(b => b.Folder == result.SafetyBackupFolder);
        Assert.StartsWith("Copia automática antes de restaurar", safety.Manifest.Note);
        var saved = Assert.Single(safety.Manifest.Entries);
        Assert.Equal("Otras instrucciones.", _fs.Files[Path.Combine(safety.Folder, saved.StoredFile)]);
    }

    [Fact]
    public void Restore_DoesNotReplaceAFile_ThatCouldNotBeSavedFirst()
    {
        var backup = _backups.Create(_document);
        // Ahora el settings.json actual tiene una clave: la copia automática lo omitiría y se perdería.
        const string current = """{ "env": { "ANTHROPIC_API_KEY": "abc123abc123abc123abc" }, "model": "sonnet" }""";
        _fs.Files[MainSettings] = current;
        _now = _now.AddMinutes(5);
        var settings = backup.Manifest.Entries.Single(e => e.Kind == BackupItemKind.CodeSettings);

        var result = _backups.Restore(backup, [settings]);

        Assert.Equal(0, result.Restored);
        Assert.Contains(result.Errors, e => e.Contains("clave"));
        Assert.Equal(current, _fs.Files[MainSettings]);
    }

    [Fact]
    public void Restore_RefusesATamperedOrInvalidCopy()
    {
        var backup = _backups.Create(_document);
        var profiles = backup.Manifest.Entries.Single(e => e.Kind == BackupItemKind.ToolProfiles);
        var memory = backup.Manifest.Entries.Single(e => e.Kind == BackupItemKind.CodeMemory);
        _fs.Files[Path.Combine(backup.Folder, memory.StoredFile)] = "modificado a mano";

        var result = _backups.Restore(backup, [memory]);

        Assert.Equal(0, result.Restored);
        Assert.Contains(result.Errors, e => e.Contains("cambió"));
        Assert.Null(result.SafetyBackupFolder);
        Assert.NotNull(profiles);
    }
}

public class UpdateWatcherTests
{
    private static ClaudeInstall Install(string version) => TestData.Install() with { Version = Version.Parse(version) };

    private static readonly TestedVersions Tested = TestedVersions.Parse("""{ "claudeDesktop": [ { "version": "2.42304.0.0" } ] }""");

    [Fact]
    public void FirstSight_IsRememberedWithoutWarning()
    {
        var state = new VersionWatchState();
        Assert.True(UpdateWatcher.RememberFirstSight(Install("2.42304.0.0"), state));
        Assert.False(UpdateWatcher.Evaluate(Install("2.42304.0.0"), state, Tested).ChangedSinceKnown);
        Assert.False(UpdateWatcher.RememberFirstSight(Install("2.42304.0.0"), state));
    }

    [Fact]
    public void NewVersion_IsAnnouncedUntilChecked()
    {
        var state = new VersionWatchState { KnownVersion = "2.42304.0.0" };
        var newer = Install("2.50000.0.0");

        var status = UpdateWatcher.Evaluate(newer, state, Tested);
        Assert.True(status.ChangedSinceKnown);
        Assert.Equal(Version.Parse("2.42304.0.0"), status.Previous);
        Assert.False(status.IsTested);
        Assert.Equal(Version.Parse("2.42304.0.0"), status.NewestTested);

        UpdateWatcher.MarkChecked(newer, state, DateTime.UtcNow);
        var after = UpdateWatcher.Evaluate(newer, state, Tested);
        Assert.False(after.ChangedSinceKnown);
        Assert.True(after.CheckedOnThisPc);
    }

    [Fact]
    public void Store_RoundTripsAndToleratesDamage()
    {
        var fs = new FakeFileSystem();
        var store = new VersionWatchStore(new AppPaths(@"C:\cma"), fs);
        store.Save(new VersionWatchState { KnownVersion = "2.1.0.0" });
        Assert.Equal("2.1.0.0", store.Load().KnownVersion);

        fs.Files[store.FilePath] = "{ roto";
        Assert.Null(store.Load().KnownVersion);
    }
}

public class CompatibilityCheckerTests
{
    private readonly FakeEnvironment _env = new();
    private readonly FakeFileSystem _fs = new();
    private readonly FakeLockProbe _lock = new();
    private readonly CompatibilityChecker _checker;

    public CompatibilityCheckerTests()
    {
        var query = new FakePackageQuery(new PackageInfo(TestData.Install().PackageFullName, TestData.InstallLocation));
        _fs.Files[TestData.Install().ExePath] = "";
        _fs.Directories.Add(FakeEnvironment.AppData + @"\Claude");
        _checker = new CompatibilityChecker(new InstallLocator(query, _fs), new DataDirResolver(_env, _fs), _lock, _fs, _env);
    }

    [Fact]
    public void AllGood_WhenFoldersAreWhereExpected()
    {
        var pro = TestData.Isolated("Claude Pro", "pro");
        pro.LastLaunchedUtc = DateTime.UtcNow;
        _fs.Directories.Add(FakeEnvironment.LocalAppData + @"\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\CMA-pro");

        var items = _checker.Run(TestData.Document(TestData.MainProfile(), pro));

        Assert.All(items, i => Assert.True(i.Ok, i.Name + ": " + i.Detail));
    }

    [Fact]
    public void UsedProfileWithoutItsDataFolder_Fails()
    {
        var pro = TestData.Isolated("Claude Pro", "pro");
        pro.LastLaunchedUtc = DateTime.UtcNow;

        var item = _checker.Run(TestData.Document(pro)).Single(i => i.Name.Contains("datos de Claude Desktop"));

        Assert.False(item.Ok);
        Assert.Contains("no abras este perfil", item.Detail);
    }

    [Fact]
    public void NeverOpenedProfile_IsFine()
    {
        var item = _checker.Run(TestData.Document(TestData.Isolated("Claude Nuevo", "nuevo")))
            .Single(i => i.Name.Contains("datos de Claude Desktop"));
        Assert.True(item.Ok);
    }

    [Fact]
    public void ClaudeMissing_Fails()
    {
        var checker = new CompatibilityChecker(new InstallLocator(new FakePackageQuery(), _fs), new DataDirResolver(_env, _fs), _lock, _fs, _env);
        var item = Assert.Single(checker.Run(TestData.Document()));
        Assert.False(item.Ok);
    }
}

using ClaudeMultiAccount.Core.Native;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Setup;
using ClaudeMultiAccount.Core.Shortcuts;

namespace ClaudeMultiAccount.Core.Tests;

internal sealed class FakeSetupHost : ISetupHost
{
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string From, string To)> Copies { get; } = [];
    public List<string> Deleted { get; } = [];
    public List<string> DeletedAfterExit { get; } = [];
    public Dictionary<string, UninstallEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public long CopyDirectory(string source, string destination)
    {
        Copies.Add((source, destination));
        Directories.Add(destination);
        return 150L * 1024 * 1024;
    }

    public void DeleteDirectory(string path)
    {
        Deleted.Add(path);
        Directories.Remove(path);
    }

    public void DeleteDirectoryAfterExit(string path) => DeletedAfterExit.Add(path);

    public void WriteUninstallEntry(string key, UninstallEntry entry) => Entries[key] = entry;

    public void DeleteUninstallEntry(string key) => Entries.Remove(key);

}

public class InstallerTests
{
    private const string Local = FakeEnvironment.LocalAppData;
    private const string Programs = FakeEnvironment.AppData + @"\Microsoft\Windows\Start Menu\Programs";
    private const string Portable = @"C:\Descargas\ClaudeMultiAccount";
    private static readonly string InstallDir = Installer.DefaultInstallDir(Local);

    private readonly FakeSetupHost _host = new();
    private readonly FakeShortcutFile _links = new();
    private readonly FakeIconWriter _icons = new();
    private readonly AppPaths _paths = new(Local + @"\ClaudeMultiAccount");
    private readonly Installer _installer;
    private readonly Profile _pro = TestData.Isolated("Claude Pro", "pro");
    private readonly string _proShortcut = FakeEnvironment.UserProfile + @"\Desktop\Claude Pro.lnk";

    public InstallerTests()
    {
        _installer = new Installer(_host, _links, _paths, "0.1.0");
        _pro.Shortcuts = [_proShortcut];
        _links.Files[_proShortcut] = new ShortcutTarget(Portable + @"\ClaudeMA.exe", ShortcutService.ArgumentsFor(_pro.Id), Portable, "", "");
    }

    [Fact]
    public void DefaultInstallDir_IsThePerUserProgramsFolder()
    {
        Assert.Equal(Local + @"\Programs\Claude Multi-Account", InstallDir);
        Assert.True(Installer.IsRunningInstalled(InstallDir + @"\ClaudeMA.exe", InstallDir));
        Assert.False(Installer.IsRunningInstalled(Portable + @"\ClaudeMA.exe", InstallDir));
    }

    [Fact]
    public void Install_CopiesRegistersAndMovesProfileShortcutsToTheInstalledExe()
    {
        var plan = _installer.PlanInstall(Portable, InstallDir, Programs);
        var saved = new Dictionary<Guid, IReadOnlyList<string>>();

        var updated = _installer.Install(plan, TestData.Document(TestData.MainProfile(), _pro), _icons, (id, list) => saved[id] = list);

        Assert.Equal((Portable, InstallDir), Assert.Single(_host.Copies));
        var start = _links.Files[Programs + @"\Claude Multi-Account\Claude Multi-Account.lnk"];
        Assert.Equal(InstallDir + @"\ClaudeMA.exe", start.ExePath);
        Assert.Equal("", start.Arguments);

        var entry = _host.Entries[Installer.UninstallKey];
        Assert.Equal("Claude Multi-Account", entry.DisplayName);
        Assert.Equal($"\"{InstallDir}\\ClaudeMA.exe\" --uninstall", entry.UninstallString);
        Assert.Equal(150 * 1024, entry.EstimatedSizeKb);
        Assert.Contains("no es de Anthropic", entry.Publisher);

        Assert.Equal(1, updated);
        Assert.Equal(InstallDir + @"\ClaudeMA.exe", _links.Files[_proShortcut].ExePath);
        Assert.Equal(new[] { _proShortcut }, saved[_pro.Id]);
    }

    [Fact]
    public void Install_FromTheInstallFolderItself_IsRefused()
    {
        var plan = _installer.PlanInstall(InstallDir, InstallDir, Programs);
        Assert.Throws<InvalidOperationException>(() => _installer.Install(plan, new ProfileDocument(), _icons, (_, _) => { }));
        Assert.Empty(_host.Copies);
    }

    [Fact]
    public void Uninstall_KeepsDataByDefault_AndNeverTouchesClaudeFolders()
    {
        var document = TestData.Document(TestData.MainProfile(), _pro);
        var foreign = FakeEnvironment.UserProfile + @"\Desktop\Otro.lnk";
        _links.Files[foreign] = new ShortcutTarget(@"C:\Otro\otro.exe", "", "", "", "");
        _host.Directories.UnionWith([InstallDir, Programs + @"\Claude Multi-Account", _paths.Home]);
        var plan = _installer.PlanUninstall(InstallDir, Programs, document, p => p.Replace("%APPDATA%", FakeEnvironment.AppData).Replace("%USERPROFILE%", FakeEnvironment.UserProfile));
        _host.Entries[Installer.UninstallKey] = new UninstallEntry("", "", "", "", "", "", 0);

        _installer.Uninstall(plan, document, deleteToolData: false, runningFromInstallDir: true);

        Assert.False(_links.Exists(_proShortcut));
        Assert.True(_links.Exists(foreign));
        Assert.Empty(_host.Entries);
        Assert.Equal(new[] { Programs + @"\Claude Multi-Account" }, _host.Deleted);
        Assert.Equal(new[] { InstallDir }, _host.DeletedAfterExit);
        Assert.Contains(FakeEnvironment.AppData + @"\Claude", plan.ClaudeDataFolders);
        Assert.Contains(FakeEnvironment.AppData + @"\CMA-pro", plan.ClaudeDataFolders);
        Assert.DoesNotContain(_host.Deleted, d => plan.ClaudeDataFolders.Contains(d));
    }

    [Fact]
    public void Uninstall_WithDeleteToolData_RemovesOnlyTheToolDataFolder()
    {
        _host.Directories.UnionWith([InstallDir, _paths.Home]);
        var plan = _installer.PlanUninstall(InstallDir, Programs, new ProfileDocument(), p => p);

        _installer.Uninstall(plan, new ProfileDocument(), deleteToolData: true, runningFromInstallDir: false);

        Assert.Equal(new[] { _paths.Home, InstallDir }, _host.Deleted);
        Assert.Empty(_host.DeletedAfterExit);
    }

    [Theory]
    [InlineData(@"C:\Users\prueba\AppData\Local\Programs")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users\prueba\AppData\Roaming\Claude")]
    public void Uninstall_RefusesFoldersThatAreNotTheTools(string installDir)
    {
        var plan = _installer.PlanUninstall(installDir, Programs, new ProfileDocument(), p => p);
        _host.Directories.Add(installDir);

        Assert.Throws<InvalidOperationException>(() => _installer.Uninstall(plan, new ProfileDocument(), false, false));
        Assert.Empty(_host.Deleted);
    }

    [Fact]
    public void Uninstall_RefusesToDeleteAToolDataFolderWithAnotherName()
    {
        var installer = new Installer(_host, _links, new AppPaths(FakeEnvironment.AppData + @"\Claude"), "0.1.0");
        var plan = installer.PlanUninstall(InstallDir, Programs, new ProfileDocument(), p => p);

        Assert.Throws<InvalidOperationException>(() => installer.Uninstall(plan, new ProfileDocument(), deleteToolData: true, false));
        Assert.Empty(_host.Deleted);
    }
}

public class WindowsSetupHostTests
{
    [Fact]
    public void DeleteAfterExitArguments_QuotesPathForCmd()
    {
        var args = WindowsSetupHost.DeleteAfterExitArguments(@"C:\Users\Ana Prueba\AppData\Local\Programs\Claude Multi-Account");
        Assert.Equal(@"/d /c ""ping -n 4 127.0.0.1 >nul & rmdir /s /q ""C:\Users\Ana Prueba\AppData\Local\Programs\Claude Multi-Account""""", args);
        Assert.DoesNotContain("\\\"", args);
    }

    [Fact]
    public void DeleteDirectoryAfterExit_RemovesFolderWithSpaces()
    {
        // T-11: la carpeta instalada quedaba en disco porque la ruta tiene espacios.
        var root = Path.Combine(Path.GetTempPath(), $"cma prueba {Guid.NewGuid():N}");
        var target = Path.Combine(root, "Claude Multi-Account");
        Directory.CreateDirectory(Path.Combine(target, "sub carpeta"));
        File.WriteAllText(Path.Combine(target, "sub carpeta", "x.txt"), "x");
        try
        {
            new WindowsSetupHost().DeleteDirectoryAfterExit(target);

            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (Directory.Exists(target) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(250);
            }
            Assert.False(Directory.Exists(target));
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

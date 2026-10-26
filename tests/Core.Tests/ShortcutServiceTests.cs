using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Shortcuts;

namespace ClaudeMultiAccount.Core.Tests;

internal sealed class FakeShortcutFile : IShortcutFile
{
    public Dictionary<string, ShortcutTarget> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Exists(string path) => Files.ContainsKey(path);

    public ShortcutTarget? Read(string path) => Files.GetValueOrDefault(path);

    public void Write(string path, ShortcutTarget target) => Files[path] = target;

    public void Delete(string path) => Files.Remove(path);
}

internal sealed class FakeIconWriter : IProfileIconWriter
{
    public Dictionary<string, (string Color, string Glyph)> Icons { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Write(string icoPath, string colorHex, string glyph) => Icons[icoPath] = (colorHex, glyph);

    public void Delete(string icoPath) => Icons.Remove(icoPath);
}

public class ShortcutServiceTests
{
    private const string Desktop = @"C:\Users\prueba\Desktop";
    private const string StartMenu = @"C:\Users\prueba\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Claude Multi-Account";
    private const string Exe = @"C:\Herramientas\ClaudeMA\ClaudeMA.exe";

    private readonly FakeShortcutFile _files = new();
    private readonly FakeIconWriter _icons = new();
    private readonly ShortcutService _service;
    private readonly Profile _pro = new()
    {
        Id = Guid.NewGuid(),
        Name = "Claude Pro",
        Type = ProfileType.DesktopIsolated,
        IconColor = "#4A7FD9",
        IconGlyph = "P",
    };

    public ShortcutServiceTests()
    {
        _service = new ShortcutService(_files, _icons, new AppPaths(@"C:\cma"),
            location => location == ShortcutLocation.Desktop ? Desktop : StartMenu, Exe);
    }

    [Fact]
    public void Create_PointsToTheToolWithTheProfileId_NotToClaude()
    {
        var path = _service.Create(_pro, ShortcutLocation.Desktop);

        Assert.Equal(Desktop + @"\Claude Pro.lnk", path);
        var target = _files.Files[path];
        Assert.Equal(Exe, target.ExePath);
        Assert.Equal($"--launch {_pro.Id:D}", target.Arguments);
        Assert.Equal(@"C:\Herramientas\ClaudeMA", target.WorkingDirectory);
        Assert.Equal($@"C:\cma\icons\{_pro.Id:D}.ico", target.IconPath);
        Assert.Equal(("#4A7FD9", "P"), _icons.Icons[target.IconPath]);
    }

    [Fact]
    public void Create_InStartMenu_UsesTheToolFolder()
    {
        Assert.Equal(StartMenu + @"\Claude Pro.lnk", _service.Create(_pro, ShortcutLocation.StartMenu));
    }

    [Fact]
    public void Create_NeverOverwritesSomeoneElsesFile()
    {
        var foreign = new ShortcutTarget(@"C:\Otro\otro.exe", "", "", "", "");
        _files.Files[Desktop + @"\Claude Pro.lnk"] = foreign;

        var path = _service.Create(_pro, ShortcutLocation.Desktop);

        Assert.Equal(Desktop + @"\Claude Pro (2).lnk", path);
        Assert.Same(foreign, _files.Files[Desktop + @"\Claude Pro.lnk"]);
    }

    [Fact]
    public void Create_Twice_ReusesItsOwnShortcut()
    {
        var first = _service.Create(_pro, ShortcutLocation.Desktop);
        var second = _service.Create(_pro, ShortcutLocation.Desktop);
        Assert.Equal(first, second);
        Assert.Single(_files.Files);
    }

    [Fact]
    public void IsOurs_SurvivesTheToolMovingToAnotherFolder()
    {
        var path = Desktop + @"\Claude Pro.lnk";
        _files.Files[path] = new ShortcutTarget(@"D:\Antes\ClaudeMA.exe", $"--launch {_pro.Id:D}", "", "", "");
        Assert.True(_service.IsOurs(path, _pro.Id));
        Assert.False(_service.IsOurs(path, Guid.NewGuid()));
    }

    [Fact]
    public void Refresh_RenamesItsShortcutsAndRedrawsTheIcon()
    {
        var old = _service.Create(_pro, ShortcutLocation.Desktop);
        var gone = Desktop + @"\Borrado por el usuario.lnk";
        var foreign = Desktop + @"\Ajeno.lnk";
        _files.Files[foreign] = new ShortcutTarget(@"C:\Otro\otro.exe", "", "", "", "");
        _pro.Shortcuts = [old, gone, foreign];
        _pro.Name = "Claude Pro personal";
        _pro.IconColor = "#1D9E75";

        var updated = _service.Refresh(_pro);

        Assert.Equal(new[] { Desktop + @"\Claude Pro personal.lnk" }, updated);
        Assert.False(_files.Exists(old));
        Assert.True(_files.Exists(foreign));
        Assert.Equal("#1D9E75", _icons.Icons[_service.IconPathFor(_pro.Id)].Color);
    }

    [Fact]
    public void RemoveAll_DeletesOnlyItsOwnShortcutsAndTheIcon()
    {
        var mine = _service.Create(_pro, ShortcutLocation.Desktop);
        var foreign = Desktop + @"\Ajeno.lnk";
        _files.Files[foreign] = new ShortcutTarget(@"C:\Otro\otro.exe", "", "", "", "");
        _pro.Shortcuts = [mine, foreign];

        _service.RemoveAll(_pro);

        Assert.False(_files.Exists(mine));
        Assert.True(_files.Exists(foreign));
        Assert.Empty(_icons.Icons);
    }

    [Theory]
    [InlineData("Claude Pro", "Claude Pro")]
    [InlineData("Trabajo: cliente/proyecto?", "Trabajo- cliente-proyecto-")]
    [InlineData("  ...  ", "Perfil")]
    [InlineData("Nombre.", "Nombre")]
    public void SafeFileName_IsValidOnWindows(string name, string expected)
    {
        Assert.Equal(expected, ShortcutService.SafeFileName(name));
    }
}

/// <summary>Con la API real de Windows (IShellLink), en la carpeta de compilación de las pruebas (dentro del repositorio).</summary>
public class ShellLinkFileTests
{
    [Fact]
    public void WriteThenRead_RoundTripsEveryField()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "lnk-prueba-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = new ClaudeMultiAccount.Core.Native.ShellLinkFile();
            var path = Path.Combine(dir, "Claude Pro.lnk");
            var id = Guid.NewGuid();
            var exe = Path.Combine(AppContext.BaseDirectory, "ClaudeMA.exe");
            var target = new ShortcutTarget(exe, $"--launch {id:D}", AppContext.BaseDirectory,
                Path.Combine(dir, "icono.ico"), "Abre «Claude Pro» con Claude Multi-Account");

            file.Write(path, target);
            var read = file.Read(path);

            Assert.True(file.Exists(path));
            Assert.NotNull(read);
            Assert.Equal(target.ExePath, read!.ExePath, ignoreCase: true);
            Assert.Equal(target.Arguments, read.Arguments);
            Assert.Equal(Path.TrimEndingDirectorySeparator(target.WorkingDirectory), Path.TrimEndingDirectorySeparator(read.WorkingDirectory), ignoreCase: true);
            Assert.Equal(target.IconPath, read.IconPath, ignoreCase: true);
            Assert.Equal(target.Description, read.Description);

            file.Delete(path);
            Assert.False(file.Exists(path));
            Assert.Null(file.Read(path));
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

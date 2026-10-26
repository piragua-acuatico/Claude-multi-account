using System.IO;

namespace ClaudeMultiAccount.App.Tests;

/// <summary>Toda la interfaz en español: el MessageBox de Windows pondría «Yes / No» en un Windows en inglés.</summary>
public class SpanishUiTests
{
    [Fact]
    public void WindowCode_DoesNotUseTheSystemMessageBox()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeMultiAccount.slnx")))
        {
            dir = dir.Parent;
        }
        var app = Path.Combine(dir!.FullName, "src", "App");

        var offenders = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(f => File.ReadAllText(f).Contains("MessageBox.Show(", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(app, f))
            .ToList();

        Assert.True(offenders.Count == 0, "Usa MessageWindow en lugar de MessageBox.Show en: " + string.Join(", ", offenders));
    }
}

using ClaudeMultiAccount.Core.Logging;

namespace ClaudeMultiAccount.Core.Tests;

/// <summary>
/// SECURITY.md T7: el código de src/ (C# y XAML de la ventana) no puede nombrar archivos con secretos.
/// Si alguna vez hace falta nombrarlos (p. ej. la lista de exclusión del BackupService), se decide aquí a la vista.
/// </summary>
public class ForbiddenReferencesTests
{
    private static readonly string[] Forbidden =
    [
        ".credentials.json",
        "config.json",
        "Cookies",
        "Local Storage",
        ".claude.json",
        "Session Storage",
    ];

    [Fact]
    public void SourceCode_DoesNotReferenceSecretFiles()
    {
        var src = Path.Combine(FindRepoRoot(), "src");
        var offenders = new List<string>();

        var files = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories));
        foreach (var file in files)
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var name in Forbidden)
            {
                if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetRelativePath(src, file)} → {name}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Referencias prohibidas en src/:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void WindowProject_IsScanned()
    {
        // Si la ventana se moviera fuera de src/, la prueba anterior dejaría de vigilarla sin avisar.
        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "src", "App", "App.csproj")));
    }

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeMultiAccount.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}

public class RedactorTests
{
    [Fact]
    public void RedactsAnthropicKeysAndLongTokens()
    {
        var text = Redactor.Redact("clave sk-ant-api03-abcDEF_123 y token " + new string('a', 50), null);
        Assert.DoesNotContain("sk-ant-api03", text);
        Assert.DoesNotContain(new string('a', 50), text);
    }

    [Fact]
    public void KeepsProfileIdsAndReplacesUserProfile()
    {
        var id = Guid.NewGuid().ToString();
        var text = Redactor.Redact($@"perfil {id} en C:\Users\Ana Prueba\.claude-prueba", @"C:\Users\Ana Prueba");
        Assert.Contains(id, text);
        Assert.Contains(@"%USERPROFILE%\.claude-prueba", text);
    }
}

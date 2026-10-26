using System.Diagnostics;
using ClaudeMultiAccount.Core.Setup;
using Microsoft.Win32;

namespace ClaudeMultiAccount.Core.Native;

/// <summary>
/// Instalación por usuario de verdad. Solo escribe en el registro la entrada de «Aplicaciones instaladas» del
/// usuario actual (HKCU\...\Uninstall\ClaudeMultiAccount); nada en HKLM ni en las clases de archivo o de
/// protocolo (SECURITY.md §5).
/// </summary>
public sealed class WindowsSetupHost : ISetupHost
{
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public long CopyDirectory(string source, string destination)
    {
        var sourceFull = Path.GetFullPath(source);
        var destinationFull = Path.GetFullPath(destination);
        if (destinationFull.StartsWith(Path.TrimEndingDirectorySeparator(sourceFull) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("No se puede instalar dentro de la propia carpeta de la herramienta.");
        }

        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(sourceFull, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destinationFull, Path.GetRelativePath(sourceFull, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            bytes += new FileInfo(file).Length;
        }
        return bytes;
    }

    public void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// El exe en ejecución no se puede borrar: se deja un cmd oculto que espera unos segundos a que termine y
    /// borra la carpeta. La ruta se comprueba antes (sin caracteres que el intérprete de comandos interprete).
    /// </summary>
    public void DeleteDirectoryAfterExit(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.IndexOfAny(['"', '&', '|', '^', '%', '<', '>', '!', '\r', '\n']) >= 0)
        {
            throw new InvalidOperationException("La ruta de instalación tiene caracteres no admitidos; bórrala a mano.");
        }
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), DeleteAfterExitArguments(full))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        Process.Start(info)?.Dispose();
    }

    /// <summary>
    /// Línea de argumentos de cmd.exe tal cual. No se usa ArgumentList: escapa las comillas internas como \", que
    /// cmd no entiende, y rmdir fallaba en rutas con espacios (T-11, 2026-10-16). Con /c, cmd quita la primera y la
    /// última comilla y deja la ruta entre comillas normales.
    /// </summary>
    public static string DeleteAfterExitArguments(string fullPath) =>
        $"/d /c \"ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{fullPath}\"\"";

    public void WriteUninstallEntry(string key, UninstallEntry entry)
    {
        using var subKey = Registry.CurrentUser.CreateSubKey(UninstallRoot + key, writable: true);
        subKey.SetValue("DisplayName", entry.DisplayName);
        subKey.SetValue("DisplayVersion", entry.DisplayVersion);
        subKey.SetValue("Publisher", entry.Publisher);
        subKey.SetValue("InstallLocation", entry.InstallLocation);
        subKey.SetValue("DisplayIcon", entry.DisplayIcon);
        subKey.SetValue("UninstallString", entry.UninstallString);
        subKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
        subKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        subKey.SetValue("EstimatedSize", (int)Math.Min(entry.EstimatedSizeKb, int.MaxValue), RegistryValueKind.DWord);
    }

    public void DeleteUninstallEntry(string key) =>
        Registry.CurrentUser.DeleteSubKeyTree(UninstallRoot + key, throwOnMissingSubKey: false);
}

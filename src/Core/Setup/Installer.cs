using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Shortcuts;

namespace ClaudeMultiAccount.Core.Setup;

/// <summary>Datos de la entrada de «Aplicaciones instaladas» (solo del usuario actual).</summary>
public sealed record UninstallEntry(
    string DisplayName,
    string DisplayVersion,
    string Publisher,
    string InstallLocation,
    string DisplayIcon,
    string UninstallString,
    long EstimatedSizeKb);

/// <summary>Todo lo que instalar y desinstalar toca del sistema. Interfaz para poder simularlo en pruebas.</summary>
public interface ISetupHost
{
    bool DirectoryExists(string path);

    /// <summary>Copia el contenido de una carpeta (reemplazando lo que ya hubiera). Devuelve el tamaño copiado en bytes.</summary>
    long CopyDirectory(string source, string destination);

    void DeleteDirectory(string path);

    /// <summary>
    /// Borra la carpeta en cuanto termine este proceso (no se puede borrar el exe que se está ejecutando).
    /// </summary>
    void DeleteDirectoryAfterExit(string path);

    void WriteUninstallEntry(string key, UninstallEntry entry);

    void DeleteUninstallEntry(string key);
}

public sealed record InstallPlan(string Source, string InstallDir, string ExePath, string StartMenuShortcut);

public sealed record UninstallPlan(
    string InstallDir,
    string StartMenuFolder,
    IReadOnlyList<string> ProfileShortcuts,
    string ToolDataDir,
    IReadOnlyList<string> ClaudeDataFolders);

/// <summary>
/// Instalación por usuario, sin administrador (SECURITY.md §5): una carpeta en
/// %LOCALAPPDATA%\Programs, una entrada en el menú Inicio y otra en «Aplicaciones instaladas» bajo HKCU.
/// No toca la instalación de Claude, ni el registro de claude://, ni las carpetas de datos de los perfiles.
/// Desinstalar conserva por defecto los datos de la herramienta; los de Claude nunca se borran desde aquí.
/// </summary>
public sealed class Installer(ISetupHost host, IShortcutFile shortcuts, AppPaths paths, string version)
{
    public const string UninstallKey = "ClaudeMultiAccount";
    public const string DisplayName = "Claude Multi-Account";
    public const string ExeName = "ClaudeMA.exe";
    public const string ProgramsFolderName = "Claude Multi-Account";

    /// <summary>%LOCALAPPDATA%\Programs\Claude Multi-Account: la carpeta habitual de los programas por usuario.</summary>
    public static string DefaultInstallDir(string localAppData) => Path.Combine(localAppData, "Programs", ProgramsFolderName);

    public static string StartMenuFolder(string programsFolder) => Path.Combine(programsFolder, ProgramsFolderName);

    public static bool IsRunningInstalled(string runningExe, string installDir) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(runningExe))?.TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public InstallPlan PlanInstall(string sourceDir, string installDir, string programsFolder) => new(
        sourceDir,
        installDir,
        Path.Combine(installDir, ExeName),
        Path.Combine(StartMenuFolder(programsFolder), DisplayName + ".lnk"));

    /// <summary>
    /// Copia la herramienta, crea su entrada del menú Inicio y la de «Aplicaciones instaladas», y pasa los accesos
    /// directos de los perfiles a la ruta instalada. Devuelve cuántos accesos directos de perfil se actualizaron.
    /// </summary>
    public int Install(InstallPlan plan, ProfileDocument document, IProfileIconWriter icons, Action<Guid, IReadOnlyList<string>> saveShortcuts)
    {
        if (string.Equals(Path.GetFullPath(plan.Source).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(plan.InstallDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La herramienta ya se está ejecutando desde la carpeta de instalación.");
        }

        var bytes = host.CopyDirectory(plan.Source, plan.InstallDir);

        shortcuts.Write(plan.StartMenuShortcut, new ShortcutTarget(plan.ExePath, "", plan.InstallDir, plan.ExePath,
            "Abre Claude Multi-Account: un Claude Desktop por cuenta"));

        host.WriteUninstallEntry(UninstallKey, new UninstallEntry(
            DisplayName,
            version,
            "Claude Multi-Account (código abierto, no es de Anthropic)",
            plan.InstallDir,
            plan.ExePath,
            $"\"{plan.ExePath}\" --uninstall",
            bytes / 1024));

        // Los accesos directos de los perfiles apuntaban a la copia portátil: ahora abren la instalada.
        var service = new ShortcutService(shortcuts, icons, paths, _ => "", plan.ExePath);
        var updated = 0;
        foreach (var profile in document.Profiles.Where(p => p.Shortcuts.Count > 0))
        {
            var refreshed = service.Refresh(profile);
            saveShortcuts(profile.Id, refreshed);
            updated += refreshed.Count;
        }
        return updated;
    }

    public UninstallPlan PlanUninstall(string installDir, string programsFolder, ProfileDocument document, Func<string, string> expand) => new(
        installDir,
        StartMenuFolder(programsFolder),
        document.Profiles.SelectMany(p => p.Shortcuts).ToList(),
        paths.Home,
        document.Profiles
            .SelectMany(p => p.Type == ProfileType.Main
                ? new[] { KnownPaths.MainDesktopDataDir, KnownPaths.MainClaudeConfigDir }
                : new[] { p.DesktopDataDir, p.ClaudeConfigDir })
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => expand(d!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList());

    /// <summary>
    /// Quita los accesos directos de la herramienta (los suyos), su entrada del menú Inicio y la de «Aplicaciones
    /// instaladas», y programa el borrado de la carpeta de instalación. Con <paramref name="deleteToolData"/> borra
    /// también la carpeta de datos de la herramienta (perfiles, copias, registros). Nunca toca los datos de Claude.
    /// </summary>
    public void Uninstall(UninstallPlan plan, ProfileDocument document, bool deleteToolData, bool runningFromInstallDir)
    {
        // Solo se borran carpetas con el nombre exacto de la herramienta: nunca una carpeta padre ni una de Claude.
        if (!HasName(plan.InstallDir, ProgramsFolderName) || !HasName(plan.StartMenuFolder, ProgramsFolderName))
        {
            throw new InvalidOperationException("La carpeta de instalación no es la de Claude Multi-Account; no se borra nada.");
        }
        if (deleteToolData && !HasName(plan.ToolDataDir, "ClaudeMultiAccount"))
        {
            throw new InvalidOperationException("La carpeta de datos no es la de Claude Multi-Account; no se borra nada.");
        }

        foreach (var profile in document.Profiles)
        {
            foreach (var path in profile.Shortcuts.Where(p => IsOurs(p, profile.Id)))
            {
                shortcuts.Delete(path);
            }
        }
        if (host.DirectoryExists(plan.StartMenuFolder))
        {
            host.DeleteDirectory(plan.StartMenuFolder);
        }
        host.DeleteUninstallEntry(UninstallKey);

        if (deleteToolData && host.DirectoryExists(plan.ToolDataDir))
        {
            host.DeleteDirectory(plan.ToolDataDir);
        }

        if (host.DirectoryExists(plan.InstallDir))
        {
            if (runningFromInstallDir)
            {
                host.DeleteDirectoryAfterExit(plan.InstallDir);
            }
            else
            {
                host.DeleteDirectory(plan.InstallDir);
            }
        }
    }

    private static bool HasName(string folder, string name) =>
        string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))), name, StringComparison.OrdinalIgnoreCase);

    private bool IsOurs(string path, Guid profileId) =>
        shortcuts.Read(path) is { } target
        && string.Equals(Path.GetFileName(target.ExePath), ExeName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(target.Arguments.Trim(), ShortcutService.ArgumentsFor(profileId), StringComparison.OrdinalIgnoreCase);
}

using System.Diagnostics;
using System.IO;
using System.Reflection;
using ClaudeMultiAccount.App.Tray;
using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Logging;
using ClaudeMultiAccount.Core.Native;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Setup;

namespace ClaudeMultiAccount.App.Services;

/// <summary>Instalar y desinstalar la herramienta desde la propia app.</summary>
public sealed class SetupService
{
    private readonly IEnvironment _env = new SystemEnvironment();
    private readonly AppPaths _paths;
    private readonly ProfileStore _store;
    private readonly ProfileCatalog _catalog;
    private readonly Installer _installer;
    private readonly string _runningExe;
    private readonly string? _userProfile;

    public SetupService()
    {
        var fs = new PhysicalFileSystem();
        _paths = AppPaths.FromEnvironment(_env);
        _store = new ProfileStore(_paths, fs, _env);
        _catalog = new ProfileCatalog(_store);
        _installer = new Installer(new WindowsSetupHost(), new ShellLinkFile(), _paths, Version);
        _runningExe = Environment.ProcessPath ?? throw new InvalidOperationException("No se sabe dónde está ClaudeMA.exe.");
        _userProfile = _env.Get("USERPROFILE");
    }

    public static string Version =>
        (typeof(SetupService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    public string InstallDir => Installer.DefaultInstallDir(_env.Get("LOCALAPPDATA")
        ?? throw new InvalidOperationException("No está definida la variable LOCALAPPDATA."));

    private static string ProgramsFolder => Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    public bool IsRunningInstalled => Installer.IsRunningInstalled(_runningExe, InstallDir);

    /// <summary>
    /// Solo la versión empaquetada (autocontenida, con su propio .NET) se puede instalar: la compilación de
    /// desarrollo depende del SDK del equipo.
    /// </summary>
    public bool CanInstall => !IsRunningInstalled && File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll"));

    public string Display(string path) => Redactor.Redact(path, _userProfile);

    public InstallPlan PlanInstall() => _installer.PlanInstall(AppContext.BaseDirectory, InstallDir, ProgramsFolder);

    /// <summary>Devuelve cuántos accesos directos de perfil pasaron a la versión instalada.</summary>
    public int Install(InstallPlan plan)
    {
        var updated = _installer.Install(plan, _store.Load(), new ProfileIconWriter(), _catalog.SetShortcuts);
        new FileLogger(_paths.LogsDir).Info($"Claude Multi-Account {Version} instalado en {Display(plan.InstallDir)}.");
        return updated;
    }

    /// <summary>Abre la versión instalada. Quien llama debe soltar antes la instancia única y cerrarse.</summary>
    public static void StartInstalled(string exePath) =>
        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exePath)! })?.Dispose();

    public UninstallPlan PlanUninstall() =>
        _installer.PlanUninstall(InstallDir, ProgramsFolder, LoadOrEmpty(), p => PathUtil.Expand(p, _env));

    public void Uninstall(UninstallPlan plan, bool deleteToolData)
    {
        if (!deleteToolData)
        {
            new FileLogger(_paths.LogsDir).Info("Claude Multi-Account desinstalado; se conservan sus datos.");
        }
        _installer.Uninstall(plan, LoadOrEmpty(), deleteToolData, IsRunningInstalled);
    }

    private ProfileDocument LoadOrEmpty()
    {
        try
        {
            return _store.Load();
        }
        catch (ProfileStoreException)
        {
            // Con un profiles.json dañado se desinstala igual; solo no se pueden quitar sus accesos directos.
            return new ProfileDocument();
        }
    }
}

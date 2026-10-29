using ClaudeMultiAccount.Core.Abstractions;

namespace ClaudeMultiAccount.Core.Paths;

/// <summary>Carpetas de la propia herramienta. CMA_HOME permite usar otra carpeta en desarrollo.</summary>
public sealed class AppPaths
{
    public const string HomeOverrideVariable = "CMA_HOME";

    public string Home { get; }
    public string ProfilesFile => Path.Combine(Home, "profiles.json");
    public string StateFile => Path.Combine(Home, "state.json");
    public string LogsDir => Path.Combine(Home, "logs");

    /// <summary>Iconos de los accesos directos: una ruta estable que no cambia con las versiones de la herramienta.</summary>
    public string IconsDir => Path.Combine(Home, "icons");

    /// <summary>Copias de seguridad de la configuración no sensible.</summary>
    public string BackupsDir => Path.Combine(Home, "backups");

    /// <summary>
    /// Buzón de mensajes entre perfiles. Fuera de %LOCALAPPDATA% a propósito: lo escribe cma.exe lanzado por
    /// Claude Code, que corre dentro del paquete MSIX de Claude, y Windows desviaría a la carpeta privada del
    /// paquete los archivos nuevos que cree en AppData (la ventana no los vería). En %USERPROFILE% no hay desvío.
    /// </summary>
    public string MailboxDir { get; }

    public AppPaths(string home, string? mailboxDir = null)
    {
        Home = home;
        MailboxDir = mailboxDir ?? Path.Combine(home, "buzon");
    }

    public static AppPaths FromEnvironment(IEnvironment env)
    {
        var overrideHome = env.Get(HomeOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideHome) && Path.IsPathFullyQualified(overrideHome))
        {
            return new AppPaths(overrideHome);
        }

        var localAppData = env.Get("LOCALAPPDATA")
            ?? throw new InvalidOperationException("No está definida la variable LOCALAPPDATA.");
        var home = Path.Combine(localAppData, "ClaudeMultiAccount");
        var userProfile = env.Get("USERPROFILE");
        return new AppPaths(home, string.IsNullOrWhiteSpace(userProfile) || !Path.IsPathFullyQualified(userProfile)
            ? null
            : Path.Combine(userProfile, ".claude-multi-account", "buzon"));
    }
}

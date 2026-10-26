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

    public AppPaths(string home) => Home = home;

    public static AppPaths FromEnvironment(IEnvironment env)
    {
        var overrideHome = env.Get(HomeOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideHome) && Path.IsPathFullyQualified(overrideHome))
        {
            return new AppPaths(overrideHome);
        }

        var localAppData = env.Get("LOCALAPPDATA")
            ?? throw new InvalidOperationException("No está definida la variable LOCALAPPDATA.");
        return new AppPaths(Path.Combine(localAppData, "ClaudeMultiAccount"));
    }
}

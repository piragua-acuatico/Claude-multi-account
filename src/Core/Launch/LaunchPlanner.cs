using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Launch;

/// <summary>
/// Qué se va a ejecutar. Nunca se registra completo: contiene el entorno del usuario.
/// <paramref name="TempDir"/> es la carpeta temporal propia del perfil (siempre dentro de su configuración),
/// que se crea justo antes de lanzar; null en el perfil principal.
/// </summary>
public sealed record LaunchPlan(
    string ExePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> RemovedOverrides,
    string? TempDir = null);

public sealed class ProfileNotLaunchableException(string message) : Exception(message);

/// <summary>Traduce un perfil a ejecutable + argumentos + entorno. Función pura: no toca el sistema.</summary>
public static class LaunchPlanner
{
    public const string UserDataDirArgument = "--user-data-dir=";
    public const string ConfigDirVariable = "CLAUDE_CONFIG_DIR";
    public const string TempDirVariable = "CLAUDE_CODE_TMPDIR";
    public const string TempDirName = "tmp";

    public static LaunchPlan Plan(Profile profile, ClaudeInstall install, IReadOnlyDictionary<string, string> inheritedEnvironment, IEnvironment env)
    {
        var scrub = EnvScrubber.Scrub(inheritedEnvironment);
        var arguments = new List<string>();
        string? tempDir = null;

        switch (profile.Type)
        {
            case ProfileType.Main:
                // Sin argumentos, sin CLAUDE_CONFIG_DIR y sin CLAUDE_CODE_TMPDIR: Claude usa sus datos de siempre.
                break;

            case ProfileType.DesktopIsolated:
                var dataDir = ExpandRequired(profile.DesktopDataDir, "desktopDataDir", profile, env);
                var configDir = ExpandRequired(profile.ClaudeConfigDir, "claudeConfigDir", profile, env);
                arguments.Add(UserDataDirArgument + dataDir);
                scrub.Environment[ConfigDirVariable] = configDir;
                // Temporales de Claude Code separados por perfil (docs env-vars: Code añade "\claude\" a esta ruta).
                tempDir = Path.Combine(configDir, TempDirName);
                scrub.Environment[TempDirVariable] = tempDir;
                break;

            case ProfileType.Cli:
                throw new ProfileNotLaunchableException(
                    $"El perfil '{profile.Name}' es de tipo terminal, y esta versión todavía no sabe abrir ese tipo.");

            default:
                throw new ProfileNotLaunchableException($"El perfil '{profile.Name}' tiene un tipo desconocido.");
        }

        return new LaunchPlan(install.ExePath, arguments, scrub.Environment, scrub.RemovedOverrides, tempDir);
    }

    private static string ExpandRequired(string? value, string field, Profile profile, IEnvironment env)
    {
        var expanded = string.IsNullOrWhiteSpace(value) ? "" : PathUtil.Expand(value, env);
        if (!PathUtil.IsUsableAbsolute(expanded))
        {
            throw new ProfileNotLaunchableException($"El perfil '{profile.Name}' no tiene una ruta válida en {field}.");
        }
        return PathUtil.Normalize(expanded);
    }
}

using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Processes;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Updates;

public sealed record CheckItem(string Name, bool Ok, string Detail);

/// <summary>
/// Comprobación de compatibilidad tras una actualización. Solo lectura: mira que
/// el exe exista y que cada perfil siga encontrando sus carpetas donde las dejó (también la redirección de MSIX).
/// No lanza ningún perfil; si todo está bien, lo último lo comprueba el usuario abriendo uno.
/// </summary>
public sealed class CompatibilityChecker(
    InstallLocator locator,
    DataDirResolver resolver,
    ILockProbe lockProbe,
    IFileSystem fs,
    IEnvironment env)
{
    public IReadOnlyList<CheckItem> Run(ProfileDocument document)
    {
        var items = new List<CheckItem>();
        var located = locator.Locate();
        if (located.Install is not { } install)
        {
            items.Add(new CheckItem("Claude Desktop", false, located.Error ?? "No se encontró Claude Desktop."));
            return items;
        }
        items.Add(new CheckItem("Claude Desktop", true, $"Versión {install.Version}; el ejecutable está en su sitio."));

        foreach (var profile in document.Profiles)
        {
            items.AddRange(CheckProfile(profile, install));
        }
        return items;
    }

    private IEnumerable<CheckItem> CheckProfile(Profile profile, ClaudeInstall install)
    {
        var used = profile.Type == ProfileType.Main || profile.LastLaunchedUtc is not null || profile.LoginConfirmedUtc is not null;

        var desktopDir = profile.Type switch
        {
            ProfileType.Main => PathUtil.Expand(KnownPaths.MainDesktopDataDir, env),
            ProfileType.DesktopIsolated when !string.IsNullOrWhiteSpace(profile.DesktopDataDir) => PathUtil.Expand(profile.DesktopDataDir, env),
            _ => null,
        };
        if (desktopDir is not null)
        {
            var effective = resolver.ResolveEffective(desktopDir, install);
            if (fs.DirectoryExists(effective))
            {
                var lockState = lockProbe.Probe(effective);
                yield return lockState == LockState.Unknown
                    ? new CheckItem($"«{profile.Name}»: datos de Claude Desktop", false,
                        "La carpeta está, pero no se pudo comprobar si el perfil está abierto (archivo de bloqueo).")
                    : new CheckItem($"«{profile.Name}»: datos de Claude Desktop", true, "La carpeta de datos está donde se esperaba.");
            }
            else
            {
                yield return used
                    ? new CheckItem($"«{profile.Name}»: datos de Claude Desktop", false,
                        "No se encuentra la carpeta de datos de un perfil que ya se usó. Puede que la actualización haya cambiado dónde guarda Claude sus datos: no abras este perfil hasta revisarlo.")
                    : new CheckItem($"«{profile.Name}»: datos de Claude Desktop", true, "Todavía no se ha abierto: Claude creará la carpeta la primera vez.");
            }
        }

        var configDir = profile.Type == ProfileType.Main
            ? PathUtil.Expand(KnownPaths.MainClaudeConfigDir, env)
            : string.IsNullOrWhiteSpace(profile.ClaudeConfigDir) ? null : PathUtil.Expand(profile.ClaudeConfigDir, env);
        if (configDir is not null)
        {
            // Un perfil usado solo para chatear puede no tener nunca esta carpeta: que falte no es un fallo.
            yield return new CheckItem($"«{profile.Name}»: configuración de Claude Code", true,
                fs.DirectoryExists(configDir)
                    ? "La carpeta está donde se esperaba."
                    : "Todavía no existe: se crea la primera vez que abres este perfil.");
        }
    }
}

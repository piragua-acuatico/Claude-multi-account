using System.Text.RegularExpressions;
using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Paths;

namespace ClaudeMultiAccount.Core.Profiles;

/// <summary>Reglas de un perfil válido. Devuelve los errores en español; lista vacía = válido.</summary>
public static partial class ProfileValidator
{
    public const int MaxNameLength = 64;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();

    private sealed record DirEntry(Profile Owner, string Label, string Path);

    public static IReadOnlyList<string> Validate(ProfileDocument document, IEnvironment env)
    {
        var errors = new List<string>();
        var dirs = new List<DirEntry>();

        if (document.SchemaVersion != ProfileDocument.CurrentSchemaVersion)
        {
            errors.Add($"Versión de esquema no soportada: {document.SchemaVersion}.");
        }

        if (document.Profiles.Count(p => p.Type == ProfileType.Main) > 1)
        {
            errors.Add("Solo puede haber un perfil principal.");
        }

        foreach (var group in document.Profiles.GroupBy(p => p.Id).Where(g => g.Count() > 1))
        {
            errors.Add($"El identificador {group.Key} está repetido.");
        }

        foreach (var group in document.Profiles
                     .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            errors.Add($"El nombre '{group.Key}' está repetido.");
        }

        foreach (var profile in document.Profiles)
        {
            ValidateProfile(profile, env, errors, dirs);
        }

        // Ningún directorio de un perfil puede coincidir con otro ni estar dentro de otro.
        for (var i = 0; i < dirs.Count; i++)
        {
            for (var j = i + 1; j < dirs.Count; j++)
            {
                if (PathUtil.IsSameOrNested(dirs[i].Path, dirs[j].Path))
                {
                    errors.Add(ReferenceEquals(dirs[i].Owner, dirs[j].Owner)
                        ? $"Perfil '{dirs[i].Owner.Name}': {dirs[i].Label} y {dirs[j].Label} coinciden o uno está dentro del otro."
                        : $"Los perfiles '{dirs[i].Owner.Name}' y '{dirs[j].Owner.Name}' comparten o anidan carpetas ({dirs[i].Label} / {dirs[j].Label}).");
                }
            }
        }

        return errors;
    }

    private static void ValidateProfile(Profile profile, IEnvironment env, List<string> errors, List<DirEntry> dirs)
    {
        var label = string.IsNullOrWhiteSpace(profile.Name) ? profile.Id.ToString() : profile.Name;

        if (profile.Id == Guid.Empty)
        {
            errors.Add($"Perfil '{label}': falta el identificador.");
        }

        var name = profile.Name.Trim();
        if (name.Length == 0)
        {
            errors.Add("Hay un perfil sin nombre.");
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add($"Perfil '{label}': el nombre supera {MaxNameLength} caracteres.");
        }
        if (profile.Name.Any(char.IsControl))
        {
            errors.Add($"Perfil '{label}': el nombre contiene caracteres de control.");
        }

        if (!ColorPattern().IsMatch(profile.IconColor))
        {
            errors.Add($"Perfil '{label}': el color debe tener el formato #RRGGBB.");
        }

        var hasData = !string.IsNullOrWhiteSpace(profile.DesktopDataDir);
        var hasConfig = !string.IsNullOrWhiteSpace(profile.ClaudeConfigDir);

        switch (profile.Type)
        {
            case ProfileType.Main:
                if (hasData || hasConfig)
                {
                    errors.Add($"Perfil '{label}': el perfil principal usa los datos actuales de Claude y no admite carpetas propias.");
                }
                break;
            case ProfileType.DesktopIsolated:
                if (!hasData)
                {
                    errors.Add($"Perfil '{label}': falta la carpeta de datos de Desktop (desktopDataDir).");
                }
                if (!hasConfig)
                {
                    errors.Add($"Perfil '{label}': falta la carpeta de configuración de Claude Code (claudeConfigDir).");
                }
                break;
            case ProfileType.Cli:
                if (hasData)
                {
                    errors.Add($"Perfil '{label}': un perfil de terminal no usa carpeta de datos de Desktop.");
                }
                if (!hasConfig)
                {
                    errors.Add($"Perfil '{label}': falta la carpeta de configuración de Claude Code (claudeConfigDir).");
                }
                break;
            default:
                errors.Add($"Perfil '{label}': tipo desconocido.");
                break;
        }

        if (profile.Type != ProfileType.Main)
        {
            if (hasData)
            {
                CheckDir(profile, label, "desktopDataDir", profile.DesktopDataDir!, env, errors, dirs);
            }
            if (hasConfig)
            {
                CheckDir(profile, label, "claudeConfigDir", profile.ClaudeConfigDir!, env, errors, dirs);
            }
        }

        foreach (var folder in profile.WorkFolders)
        {
            if (!PathUtil.IsUsableAbsolute(PathUtil.Expand(folder, env)))
            {
                errors.Add($"Perfil '{label}': la carpeta de trabajo '{folder}' no es una ruta absoluta válida.");
            }
        }
    }

    private static void CheckDir(Profile profile, string label, string field, string value,
        IEnvironment env, List<string> errors, List<DirEntry> dirs)
    {
        var expanded = PathUtil.Expand(value, env);
        if (!PathUtil.IsUsableAbsolute(expanded))
        {
            errors.Add($"Perfil '{label}': {field} no es una ruta absoluta válida o tiene variables sin resolver.");
            return;
        }

        // Las dos carpetas del perfil principal no se pueden reutilizar ni anidar.
        foreach (var protectedDir in new[] { KnownPaths.MainDesktopDataDir, KnownPaths.MainClaudeConfigDir })
        {
            var expandedProtected = PathUtil.Expand(protectedDir, env);
            if (PathUtil.IsUsableAbsolute(expandedProtected) && PathUtil.IsSameOrNested(expanded, expandedProtected))
            {
                errors.Add($"Perfil '{label}': {field} coincide con los datos del perfil principal ({protectedDir}) o los contiene.");
                return;
            }
        }

        // Tampoco dentro de las carpetas de la herramienta: desinstalarla «borrando sus datos» se llevaría la cuenta.
        foreach (var toolDir in ToolFolders(env))
        {
            if (PathUtil.IsSameOrNested(expanded, toolDir))
            {
                errors.Add($"Perfil '{label}': {field} no puede estar dentro de las carpetas de Claude Multi-Account ni contenerlas.");
                return;
            }
        }

        dirs.Add(new DirEntry(profile, field, expanded));
    }

    private static IEnumerable<string> ToolFolders(IEnvironment env)
    {
        string? home = null;
        try
        {
            home = AppPaths.FromEnvironment(env).Home;
        }
        catch (InvalidOperationException)
        {
            // Sin LOCALAPPDATA no hay carpeta de datos que proteger.
        }
        var installed = PathUtil.Expand(@"%LOCALAPPDATA%\Programs\Claude Multi-Account", env);
        return new[] { home, installed }.OfType<string>().Where(PathUtil.IsUsableAbsolute);
    }
}

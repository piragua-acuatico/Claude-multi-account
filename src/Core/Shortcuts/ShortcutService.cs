using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Shortcuts;

public enum ShortcutLocation
{
    Desktop,
    StartMenu,
}

/// <summary>Lo que guarda un .lnk. Los argumentos van separados del ejecutable, nunca en una línea de shell.</summary>
public sealed record ShortcutTarget(string ExePath, string Arguments, string WorkingDirectory, string IconPath, string Description);

/// <summary>Leer y escribir archivos .lnk. Interfaz para poder simularlo en pruebas.</summary>
public interface IShortcutFile
{
    bool Exists(string path);

    /// <summary>Null si no existe o no se puede leer como acceso directo.</summary>
    ShortcutTarget? Read(string path);

    /// <summary>Crea o reemplaza el acceso directo (y su carpeta, si falta).</summary>
    void Write(string path, ShortcutTarget target);

    void Delete(string path);
}

/// <summary>Dibuja el icono de un perfil (su color y su inicial) en un .ico.</summary>
public interface IProfileIconWriter
{
    void Write(string icoPath, string colorHex, string glyph);

    void Delete(string icoPath);
}

/// <summary>
/// Accesos directos por perfil: apuntan a la propia herramienta (<c>ClaudeMA.exe --launch &lt;id&gt;</c>),
/// nunca al exe de Claude, así que siguen funcionando aunque Claude se actualice y cambie de carpeta.
/// Solo toca los .lnk que creó ella misma: un archivo con el mismo nombre que no es suyo se respeta.
/// </summary>
public sealed class ShortcutService(
    IShortcutFile files,
    IProfileIconWriter icons,
    AppPaths paths,
    Func<ShortcutLocation, string> folderOf,
    string toolExePath)
{
    public const string ToolExeName = "ClaudeMA.exe";
    private const int MaxFileNameLength = 80;

    public static string ArgumentsFor(Guid profileId) => $"--launch {profileId:D}";

    public string IconPathFor(Guid profileId) => Path.Combine(paths.IconsDir, $"{profileId:D}.ico");

    /// <summary>Crea el acceso directo y devuelve su ruta (con " (2)", " (3)"… si el nombre lo ocupa otro archivo).</summary>
    public string Create(Profile profile, ShortcutLocation location)
    {
        icons.Write(IconPathFor(profile.Id), profile.IconColor, Glyph(profile));
        var path = FreePath(folderOf(location), profile);
        files.Write(path, TargetFor(profile));
        return path;
    }

    /// <summary>
    /// Tras editar el perfil: vuelve a dibujar el icono y renombra sus accesos directos con el nombre nuevo.
    /// Devuelve la lista actualizada (sin los que el usuario ya borró o que ya no son de la herramienta).
    /// </summary>
    public IReadOnlyList<string> Refresh(Profile profile)
    {
        var mine = profile.Shortcuts.Where(p => IsOurs(p, profile.Id)).ToList();
        if (mine.Count == 0)
        {
            return [];
        }

        icons.Write(IconPathFor(profile.Id), profile.IconColor, Glyph(profile));
        var result = new List<string>();
        foreach (var old in mine)
        {
            var folder = Path.GetDirectoryName(old)!;
            var wanted = Path.Combine(folder, SafeFileName(profile.Name) + ".lnk");
            var path = string.Equals(old, wanted, StringComparison.OrdinalIgnoreCase) ? old : FreePath(folder, profile);
            files.Write(path, TargetFor(profile));
            if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase))
            {
                files.Delete(old);
            }
            result.Add(path);
        }
        return result;
    }

    /// <summary>Al quitar el perfil: borra sus accesos directos (solo los suyos) y su icono.</summary>
    public void RemoveAll(Profile profile)
    {
        foreach (var path in profile.Shortcuts.Where(p => IsOurs(p, profile.Id)))
        {
            files.Delete(path);
        }
        icons.Delete(IconPathFor(profile.Id));
    }

    /// <summary>Un .lnk es de la herramienta si abre ClaudeMA.exe con --launch de ese perfil (aunque el exe haya cambiado de carpeta).</summary>
    public bool IsOurs(string path, Guid profileId) =>
        files.Read(path) is { } target
        && string.Equals(Path.GetFileName(target.ExePath), ToolExeName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(target.Arguments.Trim(), ArgumentsFor(profileId), StringComparison.OrdinalIgnoreCase);

    /// <summary>Nombre de archivo válido en Windows a partir del nombre del perfil.</summary>
    public static string SafeFileName(string profileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(profileName.Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c).ToArray())
            .Trim().TrimEnd('.').Trim();
        if (cleaned.Length > MaxFileNameLength)
        {
            cleaned = cleaned[..MaxFileNameLength].TrimEnd();
        }
        return cleaned.Length == 0 ? "Perfil" : cleaned;
    }

    private ShortcutTarget TargetFor(Profile profile) => new(
        toolExePath,
        ArgumentsFor(profile.Id),
        Path.GetDirectoryName(toolExePath) ?? "",
        IconPathFor(profile.Id),
        $"Abre «{profile.Name}» con Claude Multi-Account");

    private string FreePath(string folder, Profile profile)
    {
        var name = SafeFileName(profile.Name);
        for (var n = 1; ; n++)
        {
            var path = Path.Combine(folder, n == 1 ? $"{name}.lnk" : $"{name} ({n}).lnk");
            if (!files.Exists(path) || IsOurs(path, profile.Id))
            {
                return path;
            }
        }
    }

    private static string Glyph(Profile profile) =>
        !string.IsNullOrWhiteSpace(profile.IconGlyph) ? profile.IconGlyph.Trim()
        : profile.Name.Trim().Length > 0 ? profile.Name.Trim()[..1].ToUpperInvariant() : "?";
}

using System.Globalization;
using System.Text;

namespace ClaudeMultiAccount.Core.Profiles;

/// <summary>Carpetas de datos de un perfil aislado nuevo.</summary>
public sealed record ProfileFolders(string DesktopDataDir, string ClaudeConfigDir);

/// <summary>
/// Nombres de las carpetas de un perfil nuevo, con el prefijo de la herramienta para reconocerlas
/// (decisión del usuario, 2026-10-16): "Claude Trabajo" → %APPDATA%\Multi-Claude-Trabajo y
/// %USERPROFILE%\.multi-claude-trabajo. Solo letras ASCII, cifras y guiones: nada que el shell o Claude
/// puedan interpretar (SECURITY.md T15). Se fijan al crear el perfil y no cambian al renombrarlo.
/// </summary>
public static class ProfileFolderNames
{
    public const string DataPrefix = @"%APPDATA%\Multi-Claude-";
    public const string ConfigPrefix = @"%USERPROFILE%\.multi-claude-";
    public const int MaxBaseLength = 40;
    private const string Fallback = "Perfil";

    /// <summary>"Claude Trabajo" → "Trabajo"; "mi cuenta 2" → "Mi-Cuenta-2"; "Diseño" → "Diseno".</summary>
    public static string BaseName(string profileName)
    {
        var words = SplitWords(RemoveDiacritics(profileName));
        // "Claude" ya va en el prefijo; se quita si queda algo más.
        if (words.Count > 1 && string.Equals(words[0], "claude", StringComparison.OrdinalIgnoreCase))
        {
            words.RemoveAt(0);
        }
        if (words.Count == 0)
        {
            return Fallback;
        }

        var joined = string.Join('-', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        return joined.Length <= MaxBaseLength ? joined : joined[..MaxBaseLength].TrimEnd('-');
    }

    public static ProfileFolders For(string baseName) =>
        new(DataPrefix + baseName, ConfigPrefix + baseName.ToLowerInvariant());

    /// <summary>
    /// Carpetas libres para un perfil nuevo: si ya las usa otro perfil o existen en disco (p. ej. de un perfil
    /// quitado, cuyos datos se conservan), se añade -2, -3… para no mezclar nunca dos cuentas.
    /// </summary>
    /// <param name="isTaken">Recibe la ruta sin expandir; true si no se puede usar.</param>
    public static ProfileFolders Suggest(string profileName, Func<string, bool> isTaken)
    {
        var baseName = BaseName(profileName);
        for (var n = 1; ; n++)
        {
            var candidate = For(n == 1 ? baseName : $"{baseName}-{n}");
            if (!isTaken(candidate.DesktopDataDir) && !isTaken(candidate.ClaudeConfigDir))
            {
                return candidate;
            }
        }
    }

    private static string RemoveDiacritics(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static List<string> SplitWords(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }
        return words;
    }
}

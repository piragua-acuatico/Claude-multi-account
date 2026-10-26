using System.Text.RegularExpressions;
using ClaudeMultiAccount.Core.Abstractions;

namespace ClaudeMultiAccount.Core.Paths;

public static partial class PathUtil
{
    [GeneratedRegex("%([^%]+)%")]
    private static partial Regex EnvToken();

    /// <summary>Sustituye %VARIABLE% por su valor. Las variables desconocidas se dejan tal cual.</summary>
    public static string Expand(string path, IEnvironment env) =>
        EnvToken().Replace(path, m => env.Get(m.Groups[1].Value) ?? m.Value);

    /// <summary>True si la ruta ya expandida es absoluta y no quedan variables sin resolver.</summary>
    public static bool IsUsableAbsolute(string expandedPath) =>
        !string.IsNullOrWhiteSpace(expandedPath)
        && !expandedPath.Contains('%')
        && Path.IsPathFullyQualified(expandedPath);

    /// <summary>Ruta completa sin barra final, salvo en la raíz de una unidad («D:\» sigue siendo «D:\», no «D:»).</summary>
    public static string Normalize(string absolutePath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(absolutePath));

    /// <summary>True si las dos rutas son la misma carpeta (sin distinguir mayúsculas ni barra final).</summary>
    public static bool AreSame(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>True si ambas rutas son iguales o una está dentro de la otra (sin distinguir mayúsculas).</summary>
    public static bool IsSameOrNested(string a, string b)
    {
        var na = WithSeparator(Normalize(a));
        var nb = WithSeparator(Normalize(b));
        return na.StartsWith(nb, StringComparison.OrdinalIgnoreCase)
            || nb.StartsWith(na, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Si <paramref name="path"/> está dentro de <paramref name="root"/>, devuelve la parte relativa.</summary>
    public static bool TryGetRelative(string root, string path, out string relative)
    {
        var nRoot = WithSeparator(Normalize(root));
        var nPath = Normalize(path);
        if (nPath.StartsWith(nRoot, StringComparison.OrdinalIgnoreCase))
        {
            relative = nPath[nRoot.Length..];
            return relative.Length > 0;
        }
        relative = "";
        return false;
    }

    private static string WithSeparator(string normalized) =>
        Path.EndsInDirectorySeparator(normalized) ? normalized : normalized + Path.DirectorySeparatorChar;
}

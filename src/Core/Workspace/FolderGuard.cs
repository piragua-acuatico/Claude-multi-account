using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Workspace;

public enum FolderOverlap
{
    /// <summary>La misma carpeta.</summary>
    Same,

    /// <summary>La carpeta del perfil está dentro de la del otro.</summary>
    Inside,

    /// <summary>La carpeta del perfil contiene la del otro.</summary>
    Contains,
}

/// <summary>Una carpeta de trabajo del perfil que choca con la de otro perfil abierto.</summary>
public sealed record FolderConflict(Profile Other, string Folder, string OtherFolder, FolderOverlap Overlap);

/// <summary>
/// Regla de oro: dos agentes no deben trabajar en la misma carpeta a la vez.
/// Compara las carpetas de trabajo que el usuario puso en cada perfil; la herramienta no sabe qué carpeta
/// elige luego cada sesión de Code dentro de Claude.
/// </summary>
public static class FolderGuard
{
    public static IReadOnlyList<FolderConflict> FindConflicts(Profile profile, IEnumerable<Profile> activeOthers, IEnvironment env)
    {
        var mine = Usable(profile, env);
        var conflicts = new List<FolderConflict>();
        foreach (var other in activeOthers.Where(o => o.Id != profile.Id))
        {
            foreach (var (theirs, theirsExpanded) in Usable(other, env))
            {
                foreach (var (folder, expanded) in mine)
                {
                    if (!PathUtil.IsSameOrNested(expanded, theirsExpanded))
                    {
                        continue;
                    }
                    var a = PathUtil.Normalize(expanded);
                    var b = PathUtil.Normalize(theirsExpanded);
                    var overlap = string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? FolderOverlap.Same
                        : a.Length > b.Length ? FolderOverlap.Inside
                        : FolderOverlap.Contains;
                    conflicts.Add(new FolderConflict(other, folder, theirs, overlap));
                }
            }
        }
        return conflicts;
    }

    private static List<(string Folder, string Expanded)> Usable(Profile profile, IEnvironment env) =>
        profile.WorkFolders
            .Select(f => (Folder: f, Expanded: PathUtil.Expand(f, env)))
            .Where(f => PathUtil.IsUsableAbsolute(f.Expanded))
            .ToList();
}

/// <summary>
/// Archivos de un proyecto que leen todas las cuentas que lo abren (SECURITY.md T5): sus instrucciones
/// y permisos valen para cualquier perfil. Solo se mira si existen; nunca se lee su contenido.
/// </summary>
public static class SharedProjectFiles
{
    public static readonly IReadOnlyList<string> Files = ["CLAUDE.md", ".mcp.json"];
    public const string ClaudeFolder = ".claude";

    public static IReadOnlyList<string> Find(string folder, IFileSystem fs)
    {
        var found = Files.Where(f => fs.FileExists(Path.Combine(folder, f))).ToList();
        if (fs.DirectoryExists(Path.Combine(folder, ClaudeFolder)))
        {
            found.Add(ClaudeFolder + Path.DirectorySeparatorChar);
        }
        return found;
    }
}

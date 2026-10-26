using ClaudeMultiAccount.Core.Workspace;

namespace ClaudeMultiAccount.App.ViewModels;

public enum FolderConflictChoice
{
    Cancel,
    OpenAnyway,
    CreateWorktree,
}

/// <summary>
/// Aviso antes de abrir un perfil cuya carpeta de trabajo ya usa otro perfil abierto: crear una copia de trabajo
/// de Git, abrir igualmente o cancelar.
/// </summary>
public sealed class FolderConflictViewModel
{
    public FolderConflictViewModel(string profileName, IReadOnlyList<FolderConflict> conflicts,
        IReadOnlyDictionary<string, IReadOnlyList<string>> sharedFiles, WorktreeProposal? worktree)
    {
        ProfileName = profileName;
        Worktree = worktree;
        ConflictLines = conflicts.Select(c => c.Overlap switch
        {
            FolderOverlap.Same => $"«{c.Other.Name}», que está abierto, usa la misma carpeta: {c.Folder}",
            FolderOverlap.Inside => $"{c.Folder} está dentro de {c.OtherFolder}, que usa «{c.Other.Name}» (abierto).",
            _ => $"{c.Folder} contiene {c.OtherFolder}, que usa «{c.Other.Name}» (abierto).",
        }).ToList();
        SharedFileLines = sharedFiles
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => $"{kv.Key} tiene {string.Join(", ", kv.Value)}: sus instrucciones y permisos valen para todas las cuentas que la abran.")
            .ToList();
    }

    public string ProfileName { get; }

    public string Title => $"Carpeta en uso: «{ProfileName}»";

    public IReadOnlyList<string> ConflictLines { get; }

    public IReadOnlyList<string> SharedFileLines { get; }

    public bool HasSharedFiles => SharedFileLines.Count > 0;

    public WorktreeProposal? Worktree { get; }

    public bool CanCreateWorktree => Worktree is not null;

    public string WorktreeText => Worktree is { } w
        ? $"Se creará la carpeta {w.WorktreePath} con la rama {w.Branch}, a partir de {(w.BaseBranch is null ? "la versión actual" : "la rama " + w.BaseBranch)}. " +
          "Lo que no tengas en un commit no pasa a la copia. Después, en la pestaña Code de este perfil, elige esa carpeta; " +
          "cuando termines, junta los cambios con un merge o un PR."
        : "No se puede crear una copia de trabajo: la carpeta no es un repositorio de Git, o Git no está instalado.";

    public string Explanation =>
        "Si dos Claude trabajan a la vez en la misma carpeta, uno puede pisar los cambios del otro. " +
        "La herramienta solo compara las carpetas de trabajo que pusiste en cada perfil; no sabe qué carpeta elige cada chat de Code.";
}

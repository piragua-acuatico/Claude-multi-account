using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Workspace;

public sealed class GitException(string message) : IOException(message);

/// <summary>Lo que la herramienta necesita de Git. Interfaz para poder simularlo en pruebas.</summary>
public interface IGitTool
{
    /// <summary>Si hay un git utilizable en el PATH.</summary>
    bool IsAvailable();

    /// <summary>Carpeta raíz del repositorio que contiene <paramref name="folder"/>, o null si no es un repositorio.</summary>
    string? FindRepositoryRoot(string folder);

    /// <summary>Rama actual, o null si no se pudo saber (p. ej. HEAD separado).</summary>
    string? CurrentBranch(string repositoryRoot);

    bool BranchExists(string repositoryRoot, string branch);

    /// <summary><c>git worktree add -b &lt;rama&gt; &lt;ruta&gt;</c> desde la rama actual. Lanza <see cref="GitException"/> si falla.</summary>
    void AddWorktree(string repositoryRoot, string path, string branch);
}

/// <summary>Una copia de trabajo propuesta para que un perfil no trabaje en la misma carpeta que otro.</summary>
public sealed record WorktreeProposal(string RepositoryRoot, string Folder, string WorktreePath, string Branch, string? BaseBranch);

/// <summary>
/// Copia de trabajo (git worktree) por perfil: el segundo perfil trabaja en otra carpeta y otra
/// rama del mismo repositorio y luego se juntan con merge o PR. Invoca el git instalado; no reimplementa Git.
/// </summary>
public static class WorktreePlanner
{
    public const string BranchPrefix = "multi/";

    /// <summary>
    /// Carpeta hermana del repositorio ("C:\web" → "C:\web-pro") y rama "multi/pro"; con -2, -3… si ya existen.
    /// Solo letras ASCII, cifras y guiones (ProfileFolderNames), así que no hace falta escapar nada.
    /// </summary>
    public static WorktreeProposal Propose(string repositoryRoot, string folder, Profile profile, string? baseBranch,
        Func<string, bool> pathExists, Func<string, bool> branchExists)
    {
        var slug = ProfileFolderNames.BaseName(profile.Name).ToLowerInvariant();
        var root = repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? slug : $"{slug}-{n}";
            var path = $"{root}-{suffix}";
            var branch = BranchPrefix + suffix;
            if (!pathExists(path) && !branchExists(branch))
            {
                return new WorktreeProposal(root, folder, path, branch, baseBranch);
            }
        }
    }

    /// <summary>La carpeta de trabajo equivalente dentro de la copia (si era una subcarpeta del repositorio).</summary>
    public static string MapFolder(WorktreeProposal proposal)
    {
        var root = Path.GetFullPath(proposal.RepositoryRoot).TrimEnd(Path.DirectorySeparatorChar);
        var folder = Path.GetFullPath(proposal.Folder).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(root, folder, StringComparison.OrdinalIgnoreCase))
        {
            return proposal.WorktreePath;
        }
        var relative = Path.GetRelativePath(root, folder);
        return relative.StartsWith("..", StringComparison.Ordinal) ? proposal.WorktreePath : Path.Combine(proposal.WorktreePath, relative);
    }
}

/// <summary>Git de verdad: procesos con argumentos en lista, sin shell, sin preguntas interactivas y con tiempo límite.</summary>
public sealed class GitTool : IGitTool
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public bool IsAvailable() => TryRun(null, ["--version"], out _, out _) == 0;

    public string? FindRepositoryRoot(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }
        return TryRun(folder, ["rev-parse", "--show-toplevel"], out var output, out _) == 0
            ? Path.GetFullPath(output.Trim())
            : null;
    }

    public string? CurrentBranch(string repositoryRoot)
    {
        if (TryRun(repositoryRoot, ["rev-parse", "--abbrev-ref", "HEAD"], out var output, out _) != 0)
        {
            return null;
        }
        var branch = output.Trim();
        return branch.Length == 0 || branch == "HEAD" ? null : branch;
    }

    public bool BranchExists(string repositoryRoot, string branch) =>
        TryRun(repositoryRoot, ["show-ref", "--verify", "--quiet", "refs/heads/" + branch], out _, out _) == 0;

    public void AddWorktree(string repositoryRoot, string path, string branch)
    {
        var code = TryRun(repositoryRoot, ["worktree", "add", "-b", branch, path], out _, out var error);
        if (code != 0)
        {
            var reason = error.Trim().Split('\n').FirstOrDefault()?.Trim();
            throw new GitException("Git no pudo crear la copia de trabajo" + (string.IsNullOrEmpty(reason) ? "." : ": " + reason));
        }
    }

    private static int TryRun(string? workingDirectory, IReadOnlyList<string> args, out string output, out string error)
    {
        output = "";
        error = "";
        var info = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDirectory is not null)
        {
            info.ArgumentList.Add("-C");
            info.ArgumentList.Add(workingDirectory);
        }
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        // Git nunca debe quedarse esperando una contraseña o una respuesta.
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return -1;
            }
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Git terminó justo después del plazo.
                }
                error = "Git tardó demasiado.";
                return -1;
            }
            output = stdout.GetAwaiter().GetResult();
            error = stderr.GetAwaiter().GetResult();
            return process.ExitCode;
        }
        catch (Win32Exception)
        {
            // No hay git en el PATH.
            return -1;
        }
    }
}

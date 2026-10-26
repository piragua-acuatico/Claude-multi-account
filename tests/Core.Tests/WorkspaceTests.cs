using System.Diagnostics;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Workspace;

namespace ClaudeMultiAccount.Core.Tests;

public class FolderGuardTests
{
    private readonly FakeEnvironment _env = new();

    private static Profile With(string name, params string[] folders) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = ProfileType.DesktopIsolated,
        WorkFolders = folders.ToList(),
    };

    [Fact]
    public void SameFolder_IsAConflict()
    {
        var max = With("Claude Max", @"C:\proyectos\web");
        var pro = With("Claude Pro", @"c:\Proyectos\Web\");

        var conflict = Assert.Single(FolderGuard.FindConflicts(pro, [max], _env));

        Assert.Same(max, conflict.Other);
        Assert.Equal(FolderOverlap.Same, conflict.Overlap);
    }

    [Fact]
    public void NestedFolders_AreConflictsBothWays()
    {
        var max = With("Claude Max", @"C:\proyectos");
        var pro = With("Claude Pro", @"C:\proyectos\web");

        Assert.Equal(FolderOverlap.Inside, Assert.Single(FolderGuard.FindConflicts(pro, [max], _env)).Overlap);
        Assert.Equal(FolderOverlap.Contains, Assert.Single(FolderGuard.FindConflicts(max, [pro], _env)).Overlap);
    }

    [Fact]
    public void SiblingFolders_WithSimilarNames_AreNotConflicts()
    {
        var max = With("Claude Max", @"C:\proyectos\web");
        var pro = With("Claude Pro", @"C:\proyectos\web-pro");

        Assert.Empty(FolderGuard.FindConflicts(pro, [max], _env));
    }

    [Fact]
    public void VariablesAreExpanded_AndUnusablePathsIgnored()
    {
        var max = With("Claude Max", @"%USERPROFILE%\web", "relativa");
        var pro = With("Claude Pro", FakeEnvironment.UserProfile + @"\web");

        Assert.Single(FolderGuard.FindConflicts(pro, [max], _env));
    }

    [Fact]
    public void TheProfileItself_IsIgnored()
    {
        var pro = With("Claude Pro", @"C:\web");
        Assert.Empty(FolderGuard.FindConflicts(pro, [pro], _env));
    }
}

public class SharedProjectFilesTests
{
    [Fact]
    public void Find_ListsOnlyWhatExists_WithoutReadingIt()
    {
        var fs = new FakeFileSystem();
        fs.Files[@"C:\web\CLAUDE.md"] = "no se lee";
        fs.Directories.Add(@"C:\web\.claude");

        Assert.Equal(new[] { "CLAUDE.md", @".claude\" }, SharedProjectFiles.Find(@"C:\web", fs));
        Assert.Empty(SharedProjectFiles.Find(@"C:\otra", fs));
    }
}

public class WorktreePlannerTests
{
    private static readonly Profile Pro = new() { Id = Guid.NewGuid(), Name = "Claude Pro" };

    [Fact]
    public void Propose_SiblingFolderAndMultiBranch()
    {
        var proposal = WorktreePlanner.Propose(@"C:\proyectos\web", @"C:\proyectos\web", Pro, "main", _ => false, _ => false);

        Assert.Equal(@"C:\proyectos\web-pro", proposal.WorktreePath);
        Assert.Equal("multi/pro", proposal.Branch);
        Assert.Equal("main", proposal.BaseBranch);
    }

    [Fact]
    public void Propose_AddsSuffixWhenFolderOrBranchIsTaken()
    {
        var proposal = WorktreePlanner.Propose(@"C:\web", @"C:\web", Pro, "main",
            path => path == @"C:\web-pro", branch => branch == "multi/pro-2");

        Assert.Equal(@"C:\web-pro-3", proposal.WorktreePath);
        Assert.Equal("multi/pro-3", proposal.Branch);
    }

    [Fact]
    public void MapFolder_KeepsTheSubfolderInsideTheCopy()
    {
        var atRoot = new WorktreeProposal(@"C:\web", @"C:\web", @"C:\web-pro", "multi/pro", "main");
        var inside = atRoot with { Folder = @"C:\web\frontend" };

        Assert.Equal(@"C:\web-pro", WorktreePlanner.MapFolder(atRoot));
        Assert.Equal(@"C:\web-pro\frontend", WorktreePlanner.MapFolder(inside));
    }
}

/// <summary>Con el git instalado, en una carpeta de la compilación de las pruebas (dentro del repositorio).</summary>
public class GitToolTests
{
    private static void Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(dir);
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        // Sin la configuración global del usuario (firmas de commit, hooks…), que podría hacer fallar la prueba.
        info.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        using var process = Process.Start(info)!;
        // Se lee antes de esperar: si git escribe mucho, esperar primero podría bloquearse.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        _ = stdout.Result;
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)}: {stderr}");
    }

    [Fact]
    public void RealGit_DetectsTheRepoAndCreatesAWorktree()
    {
        var git = new GitTool();
        if (!git.IsAvailable())
        {
            return; // Sin git en este equipo no hay nada que probar.
        }

        var baseDir = Path.Combine(AppContext.BaseDirectory, "git-prueba-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(baseDir, "web");
        try
        {
            Directory.CreateDirectory(Path.Combine(repo, "frontend"));
            File.WriteAllText(Path.Combine(repo, "frontend", "index.html"), "<p>hola</p>");
            Git(repo, "init", "-q", "-b", "main");
            Git(repo, "add", ".");
            Git(repo, "-c", "user.name=Prueba", "-c", "user.email=prueba@example.com", "commit", "-q", "-m", "inicio");

            Assert.Equal(Path.GetFullPath(repo), git.FindRepositoryRoot(Path.Combine(repo, "frontend")), ignoreCase: true);
            Assert.Null(git.FindRepositoryRoot(baseDir + "-no-existe"));
            Assert.Equal("main", git.CurrentBranch(repo));
            Assert.False(git.BranchExists(repo, "multi/pro"));

            var proposal = WorktreePlanner.Propose(repo, Path.Combine(repo, "frontend"), new Profile { Name = "Claude Pro" }, "main",
                Directory.Exists, b => git.BranchExists(repo, b));
            git.AddWorktree(repo, proposal.WorktreePath, proposal.Branch);

            Assert.True(File.Exists(Path.Combine(WorktreePlanner.MapFolder(proposal), "index.html")));
            Assert.True(git.BranchExists(repo, "multi/pro"));
            Assert.Throws<GitException>(() => git.AddWorktree(repo, proposal.WorktreePath, proposal.Branch));
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                foreach (var file in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal); // Git marca algunos archivos como de solo lectura.
                }
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }
}

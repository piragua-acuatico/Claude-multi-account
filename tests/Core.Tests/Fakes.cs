using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Processes;

namespace ClaudeMultiAccount.Core.Tests;

internal sealed class FakeEnvironment : IEnvironment
{
    public const string UserProfile = @"C:\Users\prueba";
    public const string AppData = @"C:\Users\prueba\AppData\Roaming";
    public const string LocalAppData = @"C:\Users\prueba\AppData\Local";

    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = UserProfile,
        ["APPDATA"] = AppData,
        ["LOCALAPPDATA"] = LocalAppData,
        ["PATH"] = @"C:\Windows\System32",
    };

    /// <summary>Variables que solo tiene la consola actual (p. ej. heredadas de una sesión de Claude Code).</summary>
    public Dictionary<string, string> ConsoleOnly { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool DefaultEnvironmentAvailable { get; set; } = true;

    public string? Get(string name) => ConsoleOnly.GetValueOrDefault(name) ?? Variables.GetValueOrDefault(name);

    public IReadOnlyDictionary<string, string> GetAll()
    {
        var all = new Dictionary<string, string>(Variables, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in ConsoleOnly)
        {
            all[name] = value;
        }
        return all;
    }

    /// <summary>"Variables" hace de entorno de Windows (registro + sesión).</summary>
    public IReadOnlyDictionary<string, string>? GetDefaultUserEnvironment() =>
        DefaultEnvironmentAvailable ? new Dictionary<string, string>(Variables, StringComparer.OrdinalIgnoreCase) : null;
}

internal sealed class FakeFileSystem : IFileSystem
{
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Files.ContainsKey(path);

    public bool DirectoryExists(string path) => Directories.Contains(path.TrimEnd('\\'));

    public string ReadAllText(string path) => Files[path];

    public void WriteAllTextAtomic(string path, string content)
    {
        Files[path] = content;
        // Como en disco: escribir un archivo crea sus carpetas.
        for (var dir = Path.GetDirectoryName(path); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            Directories.Add(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
    }

    public IReadOnlyList<string> EnumerateDirectories(string path)
    {
        var prefix = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Directories
            .Where(d => d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && d.IndexOf(Path.DirectorySeparatorChar, prefix.Length) < 0)
            .ToList();
    }
}

internal sealed class FakePackageQuery(params PackageInfo[] packages) : IPackageQuery
{
    public IReadOnlyList<PackageInfo> GetPackages(string packageFamilyName) => packages;
}

internal sealed class FakeLockProbe : ILockProbe
{
    public Dictionary<string, LockState> States { get; } = new(StringComparer.OrdinalIgnoreCase);

    public LockState Probe(string dataDir) => States.GetValueOrDefault(dataDir, LockState.Absent);
}

internal sealed class FakeProcessHost : IProcessHost
{
    /// <summary>Árboles vivos: raíz → PIDs (incluida la raíz).</summary>
    public Dictionary<RootProcess, List<int>> Trees { get; } = [];
    public List<LaunchPlan> Started { get; } = [];
    public List<int> Focused { get; } = [];
    public List<int> TerminatedPids { get; } = [];
    public bool ClosesOnRequest { get; set; } = true;
    private int _nextPid = 1000;

    public RootProcess Start(LaunchPlan plan)
    {
        Started.Add(plan);
        var pid = _nextPid++;
        var root = new RootProcess(pid, StartTimeFileTimeUtc: 1_000_000 + pid);
        Trees[root] = [pid];
        return root;
    }

    public IReadOnlyList<int>? GetProcessTree(RootProcess root) =>
        Trees.TryGetValue(root, out var list) && list.Count > 0 ? list.ToList() : null;

    public void RequestClose(IReadOnlyList<int> processIds)
    {
        if (ClosesOnRequest)
        {
            Remove(processIds);
        }
    }

    public void Terminate(IReadOnlyList<int> processIds)
    {
        TerminatedPids.AddRange(processIds);
        Remove(processIds);
    }

    private void Remove(IReadOnlyList<int> processIds)
    {
        foreach (var list in Trees.Values)
        {
            list.RemoveAll(processIds.Contains);
        }
    }

    public bool TryFocus(IReadOnlyList<int> processIds)
    {
        Focused.AddRange(processIds);
        return processIds.Count > 0;
    }

    /// <summary>Relanzamientos para traer al frente (no crean árbol: el Claude abierto recibe el encargo).</summary>
    public List<LaunchPlan> Activated { get; } = [];

    public void Activate(LaunchPlan plan) => Activated.Add(plan);

    /// <summary>Raíces de Claude abiertas en el sistema (con o sin la herramienta).</summary>
    public List<int> ClaudeRoots { get; } = [];

    public IReadOnlyList<int> FindRootProcesses(string exePath) => ClaudeRoots;
}

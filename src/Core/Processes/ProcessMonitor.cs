using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Processes;

public enum ProfileState
{
    Closed,
    Starting,
    Running,
    Error,
}

public sealed record ProfileStatus(
    ProfileState State,
    IReadOnlyList<int> ProcessIds,
    LockState Lock,
    string? EffectiveDataDir)
{
    /// <summary>Abierto, pero no desde esta herramienta (no hay raíz conocida): no se puede cerrar desde aquí.</summary>
    public bool OpenedOutsideTool => State == ProfileState.Running && ProcessIds.Count == 0;
}

/// <summary>
/// Estado de cada perfil. [P0] La línea de comandos de Claude no muestra --user-data-dir, así que se usan
/// dos señales: el lockfile del directorio de datos (¿está abierto?) y el árbol del proceso
/// raíz que lanzó la herramienta (¿qué PIDs?).
/// </summary>
public sealed class ProcessMonitor(
    IProcessHost host,
    ILockProbe lockProbe,
    RuntimeStateStore runtimeState,
    DataDirResolver resolver,
    IEnvironment env)
{
    public ProfileStatus GetStatus(Profile profile, ClaudeInstall? install)
    {
        var pids = GetProcessIds(profile);

        var dataDir = GetDesktopDataDir(profile);
        if (dataDir is null)
        {
            // Perfil de terminal: solo cuenta el árbol de procesos.
            return new ProfileStatus(pids.Count > 0 ? ProfileState.Running : ProfileState.Closed, pids, LockState.Absent, null);
        }

        var effective = resolver.ResolveEffective(dataDir, install);
        var lockState = lockProbe.Probe(effective);
        var state = lockState switch
        {
            LockState.Held => ProfileState.Running,
            LockState.Unknown => ProfileState.Error,
            _ => pids.Count > 0 ? ProfileState.Starting : ProfileState.Closed,
        };
        return new ProfileStatus(state, pids, lockState, effective);
    }

    /// <summary>PIDs del árbol del proceso raíz que lanzó la herramienta; vacío si no hay raíz viva.</summary>
    public IReadOnlyList<int> GetProcessIds(Profile profile)
    {
        var root = runtimeState.Get(profile.Id);
        return root is null ? [] : host.GetProcessTree(root) ?? [];
    }

    /// <summary>Directorio de datos de Desktop ya expandido, o null si el perfil no usa Desktop.</summary>
    public string? GetDesktopDataDir(Profile profile) => profile.Type switch
    {
        ProfileType.Main => PathUtil.Expand(KnownPaths.MainDesktopDataDir, env),
        ProfileType.DesktopIsolated when !string.IsNullOrWhiteSpace(profile.DesktopDataDir) =>
            PathUtil.Expand(profile.DesktopDataDir, env),
        _ => null,
    };
}

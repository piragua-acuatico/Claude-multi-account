using System.IO;
using System.Runtime.InteropServices;
using ClaudeMultiAccount.App.Tray;
using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Backup;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Logging;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Processes;
using ClaudeMultiAccount.Core.Native;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Shortcuts;
using ClaudeMultiAccount.Core.Updates;
using ClaudeMultiAccount.Core.Workspace;

namespace ClaudeMultiAccount.App.Services;

/// <summary>Implementación real: las mismas piezas del núcleo que usa cma, sin lógica propia.</summary>
public sealed class CoreProfileService : IProfileService
{
    private const string TestedVersionsResource = "tested-versions.json";

    private readonly IEnvironment _env;
    private readonly ProfileStore _store;
    private readonly ProfileCatalog _catalog;
    private readonly RuntimeStateStore _runtimeState;
    private readonly DataDirResolver _resolver;
    private readonly ShortcutService _shortcuts;
    private readonly IFileSystem _fs;
    private readonly IGitTool _git = new GitTool();
    private readonly VersionWatchStore _versions;
    private readonly CompatibilityChecker _compatibility;
    private readonly BackupService _backups;
    private readonly AppPaths _paths;
    private readonly InstallLocator _locator;
    private readonly ProcessMonitor _monitor;
    private readonly LaunchService _launcher;
    private readonly ILogger _log;
    private readonly string? _userProfile;

    // Abrir, cerrar y guardar leen y reescriben state.json y profiles.json: se hacen de uno en uno.
    // Consultar el estado no espera por ellos (las escrituras son atómicas).
    private readonly Lock _writeGate = new();

    public CoreProfileService()
    {
        _env = new SystemEnvironment();
        var fs = new PhysicalFileSystem();
        _fs = fs;
        var paths = AppPaths.FromEnvironment(_env);
        var host = new WindowsProcessHost();
        var runtimeState = new RuntimeStateStore(paths, fs);
        _runtimeState = runtimeState;
        _resolver = new DataDirResolver(_env, fs);

        _store = new ProfileStore(paths, fs, _env);
        _catalog = new ProfileCatalog(_store);
        _paths = paths;
        _versions = new VersionWatchStore(paths, fs);
        _backups = new BackupService(paths, fs, _env, () => DateTime.UtcNow);
        _shortcuts = new ShortcutService(new ShellLinkFile(), new ProfileIconWriter(), paths, FolderOf,
            Environment.ProcessPath ?? throw new InvalidOperationException("No se sabe dónde está ClaudeMA.exe."));
        _locator = new InstallLocator(new AppxPackageQuery(), fs);
        _compatibility = new CompatibilityChecker(_locator, _resolver, new LockfileProbe(), fs, _env);
        _monitor = new ProcessMonitor(host, new LockfileProbe(), runtimeState, _resolver, _env);
        _log = new FileLogger(paths.LogsDir);
        _launcher = new LaunchService(_locator, _monitor, runtimeState, host, _env, _log);
        _userProfile = _env.Get("USERPROFILE");
    }

    public string ProfilesFileDisplayPath => Redactor.Redact(_store.FilePath, _userProfile);

    public bool ProfilesFileExists => _store.Exists;

    public bool CreateProfilesFileIfMissing()
    {
        lock (_writeGate)
        {
            var created = _catalog.CreateIfMissing();
            if (created)
            {
                _log.Info("Primer arranque: profiles.json creado con el perfil principal.");
            }
            return created;
        }
    }

    public void LogUnexpected(Exception exception) => _log.Error("Error inesperado en la ventana.", exception);

    public ProfileDocument LoadProfiles() => _store.Load();

    public LocateResult LocateClaude() => _locator.Locate();

    public TestedVersions? GetTestedVersions()
    {
        try
        {
            using var stream = typeof(CoreProfileService).Assembly.GetManifestResourceStream(TestedVersionsResource);
            if (stream is null)
            {
                return null;
            }
            using var reader = new StreamReader(stream);
            return TestedVersions.Parse(reader.ReadToEnd());
        }
        catch (FormatException ex)
        {
            _log.Warn($"No se pudo leer la lista de versiones probadas: {ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<string>? GetOverridesInWindowsEnvironment()
    {
        var windowsEnv = _env.GetDefaultUserEnvironment();
        // Scrub solo devuelve nombres; los valores no salen de aquí.
        return windowsEnv is null ? null : EnvScrubber.Scrub(windowsEnv).RemovedOverrides;
    }

    public ProfileStatus GetStatus(Profile profile, ClaudeInstall? install) => _monitor.GetStatus(profile, install);

    public LaunchOutcome Launch(Profile profile)
    {
        lock (_writeGate)
        {
            return _launcher.Launch(profile);
        }
    }

    public CloseOutcome Close(Profile profile, bool force, TimeSpan timeout)
    {
        lock (_writeGate)
        {
            return _launcher.Close(profile, force, timeout);
        }
    }

    public void RecordLaunch(Guid profileId, DateTime launchedUtc)
    {
        lock (_writeGate)
        {
            _catalog.RecordLaunch(profileId, launchedUtc);
        }
    }

    public ProfileFolders PreviewNewProfileFolders(string name) => _catalog.PreviewFolders(name, FolderExists);

    public Profile CreateProfile(ProfileEdit edit)
    {
        lock (_writeGate)
        {
            var profile = _catalog.CreateIsolated(edit, FolderExists);
            _log.Info($"Perfil '{profile.Name}' ({profile.Id}) creado.");
            return profile;
        }
    }

    public void UpdateProfile(Guid profileId, ProfileEdit edit)
    {
        lock (_writeGate)
        {
            _catalog.Update(profileId, edit);
            _log.Info($"Perfil {profileId} editado.");

            // Accesos directos con el nombre, el color y la inicial nuevos.
            var profile = _store.Load().Profiles.First(p => p.Id == profileId);
            if (profile.Shortcuts.Count > 0)
            {
                try
                {
                    _catalog.SetShortcuts(profileId, _shortcuts.Refresh(profile));
                }
                catch (Exception ex) when (IsShortcutFailure(ex))
                {
                    // El cambio del perfil ya está guardado; los accesos directos siguen abriendo el perfil por su id.
                    _log.Warn($"No se pudieron actualizar los accesos directos del perfil {profileId}: {ex.GetType().Name}.");
                }
            }
        }
    }

    public void RemoveProfile(Guid profileId)
    {
        lock (_writeGate)
        {
            if (_store.Load().Profiles.FirstOrDefault(p => p.Id == profileId) is { } profile)
            {
                try
                {
                    _shortcuts.RemoveAll(profile);
                }
                catch (Exception ex) when (IsShortcutFailure(ex))
                {
                    // Un acceso directo que sobre solo dirá que el perfil ya no existe.
                    _log.Warn($"No se pudieron borrar los accesos directos del perfil {profileId}: {ex.GetType().Name}.");
                }
            }
            _catalog.Remove(profileId);
            _runtimeState.Remove(profileId);
            _log.Info($"Perfil {profileId} quitado de la lista (sus carpetas se conservan).");
        }
    }

    public void ConfirmLogin(Guid profileId, string emailHint, DateTime confirmedUtc)
    {
        lock (_writeGate)
        {
            _catalog.ConfirmLogin(profileId, emailHint, confirmedUtc);
            // El correo es texto que escribe el usuario; no se registra.
            _log.Info($"Perfil {profileId}: el usuario confirmó la cuenta en el asistente de inicio de sesión.");
        }
    }

    public UpdateStatus GetUpdateStatus(ClaudeInstall install)
    {
        lock (_writeGate)
        {
            var state = _versions.Load();
            if (UpdateWatcher.RememberFirstSight(install, state))
            {
                _versions.Save(state);
            }
            return UpdateWatcher.Evaluate(install, state, GetTestedVersions());
        }
    }

    public IReadOnlyList<CheckItem> RunCompatibilityCheck() => _compatibility.Run(_store.Load());

    public void MarkVersionChecked(ClaudeInstall install)
    {
        lock (_writeGate)
        {
            var state = _versions.Load();
            UpdateWatcher.MarkChecked(install, state, DateTime.UtcNow);
            _versions.Save(state);
            _log.Info($"Claude Desktop {install.Version}: comprobación de compatibilidad correcta en este equipo.");
        }
    }

    public string BackupsFolderDisplayPath => Redactor.Redact(_paths.BackupsDir, _userProfile);

    public BackupInfo CreateBackup()
    {
        lock (_writeGate)
        {
            var backup = _backups.Create(_store.Load());
            _log.Info($"Copia de seguridad creada: {backup.Manifest.Entries.Count} archivo(s), {backup.Manifest.Skipped.Count} omitido(s).");
            return backup;
        }
    }

    public IReadOnlyList<BackupInfo> ListBackups() => _backups.List();

    public IReadOnlyList<RestoreCandidate> PreviewRestore(BackupInfo backup) => _backups.Preview(backup);

    public RestoreResult Restore(BackupInfo backup, IReadOnlyCollection<BackupEntry> entries)
    {
        lock (_writeGate)
        {
            var result = _backups.Restore(backup, entries);
            _log.Info($"Restauración: {result.Restored} archivo(s), {result.Errors.Count} error(es).");
            return result;
        }
    }

    public IReadOnlyList<FolderConflict> FindFolderConflicts(Profile profile, IEnumerable<Profile> activeOthers) =>
        FolderGuard.FindConflicts(profile, activeOthers, _env);

    public IReadOnlyList<string> FindSharedProjectFiles(string folder)
    {
        var expanded = PathUtil.Expand(folder, _env);
        return PathUtil.IsUsableAbsolute(expanded) ? SharedProjectFiles.Find(expanded, _fs) : [];
    }

    public WorktreeProposal? ProposeWorktree(Profile profile, string folder)
    {
        var expanded = PathUtil.Expand(folder, _env);
        if (!PathUtil.IsUsableAbsolute(expanded) || !_git.IsAvailable())
        {
            return null;
        }
        var root = _git.FindRepositoryRoot(expanded);
        if (root is null)
        {
            return null;
        }
        return WorktreePlanner.Propose(root, expanded, profile, _git.CurrentBranch(root),
            path => Directory.Exists(path) || File.Exists(path),
            branch => _git.BranchExists(root, branch)) with { Folder = folder };
    }

    public void CreateWorktree(Guid profileId, WorktreeProposal proposal)
    {
        lock (_writeGate)
        {
            _git.AddWorktree(proposal.RepositoryRoot, proposal.WorktreePath, proposal.Branch);
            _log.Info($"Copia de trabajo creada para el perfil {profileId} en {Redactor.Redact(proposal.WorktreePath, _userProfile)} (rama {proposal.Branch}).");

            // A partir de ahora el perfil trabaja en la copia, no en la carpeta compartida.
            var profile = _store.Load().Profiles.FirstOrDefault(p => p.Id == profileId)
                ?? throw new ProfileStoreException("Ese perfil ya no está en profiles.json.");
            var mapped = WorktreePlanner.MapFolder(proposal with { Folder = PathUtil.Expand(proposal.Folder, _env) });
            var folders = profile.WorkFolders
                .Select(f => string.Equals(f, proposal.Folder, StringComparison.OrdinalIgnoreCase) ? mapped : f)
                .ToList();
            _catalog.Update(profileId, new ProfileEdit(profile.Name, profile.IconColor, profile.IconGlyph, profile.EmailHint, folders));
        }
    }

    public string CreateShortcut(Guid profileId, ShortcutLocation location)
    {
        lock (_writeGate)
        {
            var profile = _store.Load().Profiles.FirstOrDefault(p => p.Id == profileId)
                ?? throw new ProfileStoreException("Ese perfil ya no está en profiles.json.");
            string path;
            try
            {
                path = _shortcuts.Create(profile, location);
            }
            catch (COMException ex)
            {
                throw new IOException("Windows no pudo crear el acceso directo.", ex);
            }
            _catalog.SetShortcuts(profileId, [.. profile.Shortcuts, path]);
            _log.Info($"Acceso directo del perfil {profileId} creado en {Redactor.Redact(path, _userProfile)}.");
            return Redactor.Redact(path, _userProfile);
        }
    }

    private static bool IsShortcutFailure(Exception ex) => ex is COMException or IOException or UnauthorizedAccessException;

    private static string FolderOf(ShortcutLocation location) => location switch
    {
        ShortcutLocation.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Claude Multi-Account"),
    };

    /// <summary>
    /// Si una carpeta de perfil ya existe, también en la ruta a la que MSIX redirige %APPDATA%: así un perfil
    /// nuevo nunca reutiliza los datos (y la sesión) de uno quitado.
    /// </summary>
    private bool FolderExists(string unexpanded)
    {
        var expanded = PathUtil.Expand(unexpanded, _env);
        if (Directory.Exists(expanded))
        {
            return true;
        }
        var redirected = _resolver.GetMsixRedirect(expanded, _locator.Locate().Install);
        return redirected is not null && Directory.Exists(redirected);
    }
}

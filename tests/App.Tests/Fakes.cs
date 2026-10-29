using System.IO;
using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.App.ViewModels;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Messaging;
using ClaudeMultiAccount.Core.Processes;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Shortcuts;
using ClaudeMultiAccount.Core.Backup;
using ClaudeMultiAccount.Core.Updates;
using ClaudeMultiAccount.Core.Workspace;

namespace ClaudeMultiAccount.App.Tests;

internal sealed class FakeProfileService : IProfileService
{
    public static readonly Version TestedVersion = new(2, 42304, 0, 0);

    public ProfileDocument Document { get; set; } = new();
    public bool FileExists { get; set; } = true;
    public string? LoadError { get; set; }
    public LocateResult Claude { get; set; } = new(Install(TestedVersion), null);
    public TestedVersions? Tested { get; set; } = TestedVersions.Parse(
        $$"""{ "claudeDesktop": [ { "version": "{{TestedVersion}}" } ] }""");
    public IReadOnlyList<string>? Overrides { get; set; } = [];

    /// <summary>Estado que devuelve GetStatus por perfil; si falta, Cerrado.</summary>
    public Dictionary<Guid, ProfileStatus> Statuses { get; } = [];
    public HashSet<Guid> StatusThrows { get; } = [];
    public HashSet<Guid> StatusThrowsIo { get; } = [];

    public Func<Profile, LaunchOutcome> OnLaunch { get; set; } =
        p => new LaunchOutcome(LaunchOutcomeKind.Launched, $"'{p.Name}' abierto.", [], 1234);
    public Func<Profile, bool, CloseOutcome> OnClose { get; set; } =
        (p, _) => new CloseOutcome(CloseOutcomeKind.Terminated, $"'{p.Name}' cerrado a la fuerza.");

    public List<Guid> Launched { get; } = [];
    public List<(Guid Id, bool Force)> CloseCalls { get; } = [];
    public List<(Guid Id, DateTime When)> Recorded { get; } = [];
    public bool RecordThrows { get; set; }

    public string ProfilesFileDisplayPath => @"%LOCALAPPDATA%\ClaudeMultiAccount\profiles.json";

    public bool ProfilesFileExists => FileExists;

    /// <summary>Si el primer arranque crea profiles.json (como el servicio real) o falla al intentarlo.</summary>
    public bool CreatesFileOnInit { get; set; }
    public bool CreateThrows { get; set; }
    public List<Exception> Logged { get; } = [];

    public bool CreateProfilesFileIfMissing()
    {
        if (CreateThrows)
        {
            throw new UnauthorizedAccessException("sin permiso");
        }
        if (FileExists || !CreatesFileOnInit)
        {
            return false;
        }
        Document = ProfileCatalog.CreateInitialDocument();
        FileExists = true;
        return true;
    }

    public void LogUnexpected(Exception exception) => Logged.Add(exception);

    public bool LoadThrowsIo { get; set; }

    public ProfileDocument LoadProfiles() =>
        LoadThrowsIo ? throw new IOException("archivo en uso")
        : LoadError is null ? Document : throw new ProfileStoreException(LoadError);

    public bool ThrowOnLocate { get; set; }

    public LocateResult LocateClaude() => ThrowOnLocate ? throw new InvalidOperationException("fallo inesperado") : Claude;

    public TestedVersions? GetTestedVersions() => Tested;

    public IReadOnlyList<string>? GetOverridesInWindowsEnvironment() => Overrides;

    public ProfileStatus GetStatus(Profile profile, ClaudeInstall? install)
    {
        if (StatusThrows.Contains(profile.Id))
        {
            throw new InvalidOperationException("fallo simulado");
        }
        if (StatusThrowsIo.Contains(profile.Id))
        {
            throw new IOException("archivo en uso");
        }
        return Statuses.GetValueOrDefault(profile.Id, Closed());
    }

    public LaunchOutcome Launch(Profile profile)
    {
        Launched.Add(profile.Id);
        return OnLaunch(profile);
    }

    public CloseOutcome Close(Profile profile, bool force, TimeSpan timeout)
    {
        CloseCalls.Add((profile.Id, force));
        return OnClose(profile, force);
    }

    public void RecordLaunch(Guid profileId, DateTime launchedUtc)
    {
        if (RecordThrows)
        {
            throw new ProfileStoreException("No se guardó profiles.json.");
        }
        Recorded.Add((profileId, launchedUtc));
    }

    public List<ProfileEdit> Created { get; } = [];
    public List<(Guid Id, ProfileEdit Edit)> Updated { get; } = [];
    public List<Guid> Removed { get; } = [];
    public string? SaveError { get; set; }

    public ProfileFolders PreviewNewProfileFolders(string name)
    {
        var used = Document.Profiles.Select(p => p.DesktopDataDir).Where(d => d is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ProfileFolderNames.Suggest(name, used.Contains);
    }

    public Profile CreateProfile(ProfileEdit edit)
    {
        ThrowIfSaveFails();
        Created.Add(edit);
        var folders = PreviewNewProfileFolders(edit.Name);
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            Name = edit.Name,
            Type = ProfileType.DesktopIsolated,
            IconColor = edit.IconColor,
            IconGlyph = edit.IconGlyph,
            EmailHint = edit.EmailHint,
            WorkFolders = edit.WorkFolders.ToList(),
            DesktopDataDir = folders.DesktopDataDir,
            ClaudeConfigDir = folders.ClaudeConfigDir,
        };
        Document.Profiles.Add(profile);
        return profile;
    }

    public void UpdateProfile(Guid profileId, ProfileEdit edit)
    {
        ThrowIfSaveFails();
        Updated.Add((profileId, edit));
        var profile = Document.Profiles.Single(p => p.Id == profileId);
        profile.Name = edit.Name;
        profile.IconColor = edit.IconColor;
        profile.IconGlyph = edit.IconGlyph;
    }

    public void RemoveProfile(Guid profileId)
    {
        ThrowIfSaveFails();
        Removed.Add(profileId);
        Document.Profiles.RemoveAll(p => p.Id == profileId);
    }

    /// <summary>Estado de versiones como lo guardaría versions.json.</summary>
    public VersionWatchState VersionState { get; } = new();
    public List<CheckItem> CompatibilityItems { get; set; } = [new CheckItem("Claude Desktop", true, "bien")];
    public List<Version> VersionsMarkedChecked { get; } = [];

    public UpdateStatus GetUpdateStatus(ClaudeInstall install)
    {
        UpdateWatcher.RememberFirstSight(install, VersionState);
        return UpdateWatcher.Evaluate(install, VersionState, Tested);
    }

    public IReadOnlyList<CheckItem> RunCompatibilityCheck() => CompatibilityItems;

    public void MarkVersionChecked(ClaudeInstall install)
    {
        VersionsMarkedChecked.Add(install.Version);
        UpdateWatcher.MarkChecked(install, VersionState, DateTime.UtcNow);
    }

    /// <summary>Copias "en disco": se añaden al crear y se restauran registrando lo elegido.</summary>
    public List<BackupInfo> Backups { get; } = [];
    public Dictionary<string, IReadOnlyList<RestoreCandidate>> Previews { get; } = [];
    public List<(BackupInfo Backup, IReadOnlyCollection<BackupEntry> Entries)> Restores { get; } = [];
    public string BackupsFolderDisplayPath => @"%LOCALAPPDATA%\ClaudeMultiAccount\backups";

    public BackupInfo CreateBackup()
    {
        var info = new BackupInfo($@"C:\cma\backups\{Backups.Count + 1}", new BackupManifest
        {
            CreatedUtc = new DateTime(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc).AddMinutes(Backups.Count),
            Entries = [new BackupEntry(BackupItemKind.ToolProfiles, null, "", "profiles.json", "01-profiles.json", "A")],
            Skipped = ["«Claude Pro»: ajustes de Claude Code (settings.json): no se copió porque parece contener una clave, token o contraseña."],
        });
        Backups.Insert(0, info);
        return info;
    }

    public IReadOnlyList<BackupInfo> ListBackups() => Backups.ToList();

    public IReadOnlyList<RestoreCandidate> PreviewRestore(BackupInfo backup) =>
        Previews.TryGetValue(backup.Folder, out var preview)
            ? preview
            : backup.Manifest.Entries.Select(e => new RestoreCandidate(e, RestoreState.Changed)).ToList();

    public RestoreResult Restore(BackupInfo backup, IReadOnlyCollection<BackupEntry> entries)
    {
        Restores.Add((backup, entries));
        return new RestoreResult(entries.Count, @"C:\cma\backups\antes", []);
    }

    /// <summary>Archivos compartidos por carpeta; si falta, ninguno.</summary>
    public Dictionary<string, IReadOnlyList<string>> SharedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Carpetas que son repositorios de Git (raíz) para proponer una copia de trabajo.</summary>
    public HashSet<string> GitRepos { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(Guid Id, WorktreeProposal Proposal)> WorktreesCreated { get; } = [];
    public string? WorktreeError { get; set; }

    public IReadOnlyList<FolderConflict> FindFolderConflicts(Profile profile, IEnumerable<Profile> activeOthers) =>
        FolderGuard.FindConflicts(profile, activeOthers, new NoVariables());

    public IReadOnlyList<string> FindSharedProjectFiles(string folder) => SharedFiles.GetValueOrDefault(folder, []);

    public WorktreeProposal? ProposeWorktree(Profile profile, string folder) =>
        GitRepos.Contains(folder)
            ? WorktreePlanner.Propose(folder, folder, profile, "main", _ => false, _ => false)
            : null;

    public void CreateWorktree(Guid profileId, WorktreeProposal proposal)
    {
        if (WorktreeError is not null)
        {
            throw new GitException(WorktreeError);
        }
        WorktreesCreated.Add((profileId, proposal));
        var profile = Document.Profiles.Single(p => p.Id == profileId);
        profile.WorkFolders = profile.WorkFolders.Select(f => f == proposal.Folder ? proposal.WorktreePath : f).ToList();
    }

    /// <summary>Las pruebas usan rutas absolutas sin %VARIABLES%.</summary>
    private sealed class NoVariables : ClaudeMultiAccount.Core.Abstractions.IEnvironment
    {
        public string? Get(string name) => null;
        public IReadOnlyDictionary<string, string> GetAll() => new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string>? GetDefaultUserEnvironment() => new Dictionary<string, string>();
    }

    public List<(Guid Id, string Email)> LoginsConfirmed { get; } = [];

    public void ConfirmLogin(Guid profileId, string emailHint, DateTime confirmedUtc)
    {
        ThrowIfSaveFails();
        LoginsConfirmed.Add((profileId, emailHint));
        var profile = Document.Profiles.Single(p => p.Id == profileId);
        profile.LoginConfirmedUtc = confirmedUtc;
        profile.LastLaunchedUtc ??= confirmedUtc;
    }

    public List<(Guid Id, ShortcutLocation Location)> ShortcutsCreated { get; } = [];
    public string? ShortcutError { get; set; }

    public string CreateShortcut(Guid profileId, ShortcutLocation location)
    {
        if (ShortcutError is not null)
        {
            throw new IOException(ShortcutError);
        }
        ShortcutsCreated.Add((profileId, location));
        var profile = Document.Profiles.Single(p => p.Id == profileId);
        var path = (location == ShortcutLocation.Desktop ? @"%USERPROFILE%\Desktop\" : @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Claude Multi-Account\") + profile.Name + ".lnk";
        profile.Shortcuts.Add(path);
        return path;
    }

    public bool MessagingEnabled => Document.MessagingEnabled;

    public string MailboxFolder => @"C:\buzon-de-prueba";

    public List<MailboxEntry> Messages { get; } = [];

    public List<string> MessagingProblems { get; } = [];

    public int MessagingSyncs { get; private set; }

    public IReadOnlyList<string> SetMessaging(bool enabled)
    {
        Document.MessagingEnabled = enabled;
        return MessagingProblems;
    }

    public IReadOnlyList<string> SyncMessaging()
    {
        MessagingSyncs++;
        return Document.MessagingEnabled ? MessagingProblems : [];
    }

    public IReadOnlyList<MailboxEntry> MessageHistory(int max) => Messages.Take(max).ToList();

    public List<MailboxMessage> Unread { get; } = [];

    public IReadOnlyList<MailboxMessage> UnreadMessages() => Unread;

    public List<OpenedChat> ChatsToOpen { get; } = [];

    public IReadOnlyList<OpenedChat> OpenChatsForNewMessages(DateTime nowUtc)
    {
        var opened = ChatsToOpen.ToList();
        ChatsToOpen.Clear();
        return opened;
    }

    private void ThrowIfSaveFails()
    {
        if (SaveError is not null)
        {
            throw new ProfileStoreException(SaveError);
        }
    }

    public static ClaudeInstall Install(Version version) => new(
        InstallKind.Msix, version, $"Claude_{version}_x64__pzs8sxrjxfjjc", "Claude_pzs8sxrjxfjjc",
        @"C:\Program Files\WindowsApps\Claude", @"C:\Program Files\WindowsApps\Claude\app\Claude.exe");

    public static ProfileStatus Closed() => new(ProfileState.Closed, [], LockState.Absent, null);

    public static ProfileStatus Starting(params int[] pids) => new(ProfileState.Starting, pids, LockState.Absent, null);

    /// <summary>Abierto desde la herramienta: hay árbol de procesos.</summary>
    public static ProfileStatus RunningFromTool(params int[] pids) => new(ProfileState.Running, pids, LockState.Held, null);

    /// <summary>Abierto fuera de la herramienta: lockfile en uso, pero sin raíz conocida.</summary>
    public static ProfileStatus RunningOutside() => new(ProfileState.Running, [], LockState.Held, null);

    public static ProfileStatus LockUnknown() => new(ProfileState.Error, [], LockState.Unknown, null);
}

internal sealed class FakeDialogService(bool answer) : IDialogService
{
    public bool Answer { get; set; } = answer;
    public List<(string Title, string Message)> Asked { get; } = [];

    /// <summary>Lo que "escribe" el usuario en el editor antes de pulsar Guardar; null = pulsa Cancelar.</summary>
    public Action<ProfileEditorViewModel>? FillEditor { get; set; }
    public List<ProfileEditorViewModel> Editors { get; } = [];
    public string? NextFolder { get; set; }

    public bool EditProfile(ProfileEditorViewModel editor)
    {
        Editors.Add(editor);
        if (FillEditor is null)
        {
            return false;
        }
        FillEditor(editor);
        return editor.TryAccept();
    }

    public string? PickFolder() => NextFolder;

    /// <summary>Dónde elige el usuario crear el acceso directo; null = Cancelar.</summary>
    public ShortcutLocation? ShortcutChoice { get; set; }

    public ShortcutLocation? ChooseShortcutLocation(string profileName) => ShortcutChoice;

    /// <summary>Lo que hace el usuario en el asistente; null = lo cierra sin terminar.</summary>
    public Func<LoginWizardViewModel, Task<bool>>? DriveWizard { get; set; }
    public List<LoginWizardViewModel> Wizards { get; } = [];

    public List<SharingInfoViewModel> SharingShown { get; } = [];

    public void ShowSharingInfo(SharingInfoViewModel info) => SharingShown.Add(info);

    /// <summary>Lo que elige el usuario en el aviso de carpeta en uso.</summary>
    public FolderConflictChoice ConflictChoice { get; set; } = FolderConflictChoice.Cancel;
    public List<FolderConflictViewModel> ConflictsShown { get; } = [];

    public List<CompatibilityReportViewModel> Reports { get; } = [];
    public List<BackupsViewModel> BackupWindows { get; } = [];

    public void ShowCompatibilityReport(CompatibilityReportViewModel report) => Reports.Add(report);

    public void ShowBackups(BackupsViewModel backups) => BackupWindows.Add(backups);

    public List<MessagesViewModel> MessageWindows { get; } = [];

    public void ShowMessages(MessagesViewModel messages) => MessageWindows.Add(messages);

    public FolderConflictChoice ResolveFolderConflict(FolderConflictViewModel warning)
    {
        ConflictsShown.Add(warning);
        return ConflictChoice;
    }

    public bool RunLoginWizard(LoginWizardViewModel wizard)
    {
        Wizards.Add(wizard);
        // Las pruebas no tienen hilo de interfaz: se espera aquí lo que haría el usuario con la ventana abierta.
        return DriveWizard is not null && DriveWizard(wizard).GetAwaiter().GetResult();
    }

    public bool ConfirmDangerous(string title, string message)
    {
        Asked.Add((title, message));
        return Answer;
    }
}

internal static class TestProfiles
{
    public static Profile MainProfile() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Claude Max",
        Type = ProfileType.Main,
        IconColor = "#D97757",
        IconGlyph = "M",
    };

    public static Profile Pro() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Claude Pro",
        Type = ProfileType.DesktopIsolated,
        IconColor = "#4A7FD9",
        IconGlyph = "P",
        DesktopDataDir = @"%APPDATA%\CMA-prueba",
        ClaudeConfigDir = @"%USERPROFILE%\.claude-prueba",
        // Como el perfil Pro real: ya se abrió antes, así que no pide el asistente de inicio de sesión.
        LastLaunchedUtc = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>Perfil aislado recién creado: nunca abierto, así que la primera vez sale el asistente.</summary>
    public static Profile NewIsolated(string name = "Claude Trabajo") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = ProfileType.DesktopIsolated,
        IconColor = "#1D9E75",
        IconGlyph = "T",
        DesktopDataDir = @"%APPDATA%\Multi-Claude-Trabajo",
        ClaudeConfigDir = @"%USERPROFILE%\.multi-claude-trabajo",
    };

    public static ProfileDocument Document(params Profile[] profiles)
    {
        var document = new ProfileDocument();
        document.Profiles.AddRange(profiles);
        return document;
    }
}

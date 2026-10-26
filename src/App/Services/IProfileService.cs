using ClaudeMultiAccount.Core.Backup;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Processes;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Shortcuts;
using ClaudeMultiAccount.Core.Updates;
using ClaudeMultiAccount.Core.Workspace;

namespace ClaudeMultiAccount.App.Services;

/// <summary>
/// Lo que la ventana necesita del núcleo. Solo delega en ProfileStore, InstallLocator, ProcessMonitor y
/// LaunchService; existe para poder probar los ViewModels con un doble. Las llamadas pueden tardar: los
/// ViewModels las hacen fuera del hilo de la interfaz.
/// </summary>
public interface IProfileService
{
    /// <summary>Ruta de profiles.json para mostrar (con %USERPROFILE% en vez de la carpeta del usuario).</summary>
    string ProfilesFileDisplayPath { get; }

    bool ProfilesFileExists { get; }

    /// <summary>Primer arranque: crea profiles.json con el perfil principal si no existe. Devuelve si lo creó.</summary>
    bool CreateProfilesFileIfMissing();

    /// <summary>Apunta en el registro un error inesperado (sin datos de las cuentas).</summary>
    void LogUnexpected(Exception exception);

    /// <summary>Lanza <see cref="ProfileStoreException"/> si el archivo no se puede leer.</summary>
    ProfileDocument LoadProfiles();

    LocateResult LocateClaude();

    /// <summary>Null si no se pudo leer la lista de versiones probadas.</summary>
    TestedVersions? GetTestedVersions();

    /// <summary>
    /// Nombres (nunca valores) de las variables del entorno de Windows que se quitan al abrir un perfil.
    /// Null si Windows no dio su entorno por defecto (entonces no se puede abrir ningún perfil).
    /// </summary>
    IReadOnlyList<string>? GetOverridesInWindowsEnvironment();

    ProfileStatus GetStatus(Profile profile, ClaudeInstall? install);

    LaunchOutcome Launch(Profile profile);

    CloseOutcome Close(Profile profile, bool force, TimeSpan timeout);

    /// <summary>Guarda la fecha del último lanzamiento en profiles.json, como hace cma.</summary>
    void RecordLaunch(Guid profileId, DateTime launchedUtc);

    /// <summary>Carpetas que tendría un perfil nuevo con ese nombre (no crea nada).</summary>
    ProfileFolders PreviewNewProfileFolders(string name);

    /// <summary>Lanza <see cref="ProfileStoreException"/> si profiles.json no admite el cambio.</summary>
    Profile CreateProfile(ProfileEdit edit);

    /// <summary>Lanza <see cref="ProfileStoreException"/> si profiles.json no admite el cambio.</summary>
    void UpdateProfile(Guid profileId, ProfileEdit edit);

    /// <summary>
    /// Quita el perfil de la lista y sus accesos directos; sus carpetas y su sesión se conservan.
    /// Al editar un perfil (<see cref="UpdateProfile"/>) sus accesos directos se renombran y su icono se redibuja.
    /// </summary>
    void RemoveProfile(Guid profileId);

    /// <summary>Fin del asistente de inicio de sesión: el usuario confirmó que el perfil tiene la cuenta correcta.</summary>
    void ConfirmLogin(Guid profileId, string emailHint, DateTime confirmedUtc);

    /// <summary>Versión instalada frente a la conocida y a la lista de probadas. La primera vez la apunta sin avisar.</summary>
    UpdateStatus GetUpdateStatus(ClaudeInstall install);

    /// <summary>Comprobación de compatibilidad de solo lectura.</summary>
    IReadOnlyList<CheckItem> RunCompatibilityCheck();

    /// <summary>Apunta que la versión instalada pasó la comprobación en este equipo.</summary>
    void MarkVersionChecked(ClaudeInstall install);

    /// <summary>Copia de la configuración no sensible.</summary>
    BackupInfo CreateBackup();

    IReadOnlyList<BackupInfo> ListBackups();

    /// <summary>Qué cambiaría al restaurar.</summary>
    IReadOnlyList<RestoreCandidate> PreviewRestore(BackupInfo backup);

    /// <summary>Restaura lo elegido; antes guarda una copia de los archivos que reemplaza.</summary>
    RestoreResult Restore(BackupInfo backup, IReadOnlyCollection<BackupEntry> entries);

    /// <summary>Carpeta de las copias, para mostrar.</summary>
    string BackupsFolderDisplayPath { get; }

    /// <summary>Carpetas de trabajo del perfil que coinciden o se anidan con las de otros perfiles abiertos.</summary>
    IReadOnlyList<FolderConflict> FindFolderConflicts(Profile profile, IEnumerable<Profile> activeOthers);

    /// <summary>CLAUDE.md, .claude/ y .mcp.json que hay en la carpeta (solo nombres).</summary>
    IReadOnlyList<string> FindSharedProjectFiles(string folder);

    /// <summary>Copia de trabajo de Git propuesta para esa carpeta, o null si no es un repositorio o no hay Git.</summary>
    WorktreeProposal? ProposeWorktree(Profile profile, string folder);

    /// <summary>Crea la copia de trabajo y cambia la carpeta de trabajo del perfil por la nueva. Lanza <see cref="GitException"/>.</summary>
    void CreateWorktree(Guid profileId, WorktreeProposal proposal);

    /// <summary>Crea un acceso directo que abre el perfil con la herramienta. Devuelve su ruta para mostrar.</summary>
    string CreateShortcut(Guid profileId, ShortcutLocation location);
}

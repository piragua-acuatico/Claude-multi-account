using System.Collections.ObjectModel;
using System.IO;
using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Processes;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Shortcuts;
using ClaudeMultiAccount.Core.Updates;
using ClaudeMultiAccount.Core.Workspace;

namespace ClaudeMultiAccount.App.ViewModels;

/// <summary>
/// Ventana principal. Todo lo que toca el sistema se hace con Task.Run para no bloquear la interfaz; al volver
/// del await se sigue en el hilo de la interfaz, que es donde se cambian las colecciones.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    /// <summary>Cada cuánto se consulta el estado. Con lo que tarda la consulta queda por debajo de 3 s.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Espera tras el cierre amable antes de forzar. Corta a propósito: cerrar la ventana solo manda Claude a
    /// la bandeja; el usuario ya aceptó forzar.
    /// </summary>
    public static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(2);

    private readonly IProfileService _service;
    private readonly IDialogService _dialogs;
    private readonly Func<DateTime> _utcNow;

    private readonly List<string> _environmentNotices = [];
    private readonly List<string> _snapshotNotices = [];
    private readonly List<string> _actionNotices = [];

    private TestedVersions? _testedVersions;
    private List<Profile> _lastProfiles = [];
    private bool _refreshing;
    private readonly HashSet<string> _notifiedMessages = [];
    private string _claudeSummary = "Buscando Claude Desktop…";
    private string? _statusMessage;
    private string? _emptyMessage;
    private string? _updateBanner;
    private ClaudeInstall? _lastInstall;

    private readonly ProfileActions _actions;

    public MainViewModel(IProfileService service, IDialogService dialogs, Func<DateTime>? utcNow = null)
    {
        _service = service;
        _dialogs = dialogs;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _actions = new ProfileActions(OpenAsync, CloseAsync, EditAsync, RemoveAsync, CreateShortcutAsync);
        NewProfileCommand = new AsyncRelayCommand(NewProfileAsync);
        ShowSharingInfoCommand = new RelayCommand(ShowSharingInfo);
        CheckCompatibilityCommand = new AsyncRelayCommand(CheckCompatibilityAsync);
        ShowBackupsCommand = new RelayCommand(ShowBackups);
        ShowMessagesCommand = new RelayCommand(ShowMessages);
    }

    public AsyncRelayCommand CheckCompatibilityCommand { get; }

    public RelayCommand ShowBackupsCommand { get; }

    public RelayCommand ShowMessagesCommand { get; }

    /// <summary>Texto para la notificación de Windows cuando un perfil deja un mensaje a otro.</summary>
    public event Action<string>? MessageArrived;

    /// <summary>Aviso de actualización o de versión no probada, con su botón de comprobar; null si no hay nada que decir.</summary>
    public string? UpdateBanner
    {
        get => _updateBanner;
        private set => SetProperty(ref _updateBanner, value);
    }

    /// <summary>Comprobación de compatibilidad. Si todo va bien, la versión queda comprobada en este equipo.</summary>
    public async Task CheckCompatibilityAsync()
    {
        IReadOnlyList<CheckItem> items;
        try
        {
            items = await Task.Run(_service.RunCompatibilityCheck);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProfileStoreException)
        {
            StatusMessage = "No se pudo hacer la comprobación: " + ex.Message;
            return;
        }

        var report = new CompatibilityReportViewModel(items);
        if (report.AllOk && _lastInstall is { } install)
        {
            try
            {
                await Task.Run(() => _service.MarkVersionChecked(install));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusMessage = "La comprobación fue bien, pero no se pudo apuntar: " + ex.Message;
            }
        }
        _dialogs.ShowCompatibilityReport(report);
        StatusMessage = report.AllOk ? "Comprobación de compatibilidad: todo bien." : "Comprobación de compatibilidad: hay algo que revisar.";
        await RefreshAsync();
    }

    /// <summary>Ventana de mensajes entre cuentas.</summary>
    public void ShowMessages() => _dialogs.ShowMessages(new MessagesViewModel(_service));

    /// <summary>Ventana de copias de seguridad. Al cerrarla se vuelve a leer todo por si se restauró algo.</summary>
    public void ShowBackups()
    {
        var backups = new BackupsViewModel(_service, _dialogs);
        _ = backups.LoadAsync();
        _dialogs.ShowBackups(backups);
        _ = RefreshAsync();
    }

    public AsyncRelayCommand NewProfileCommand { get; }

    public RelayCommand ShowSharingInfoCommand { get; }

    /// <summary>Panel "Qué comparte cada perfil" con las carpetas de los perfiles actuales.</summary>
    public void ShowSharingInfo() => _dialogs.ShowSharingInfo(new SharingInfoViewModel(Profiles.Select(p => p.Profile)));

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];

    /// <summary>Avisos que se ven arriba de la lista (sin valores de variables ni datos de las cuentas).</summary>
    public ObservableCollection<string> Notices { get; } = [];

    public string ClaudeSummary
    {
        get => _claudeSummary;
        private set => SetProperty(ref _claudeSummary, value);
    }

    /// <summary>Resultado de la última acción, para la barra de estado.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Texto en lugar de la lista cuando no hay perfiles; null si los hay.</summary>
    public string? EmptyMessage
    {
        get => _emptyMessage;
        private set => SetProperty(ref _emptyMessage, value);
    }

    public async Task InitializeAsync(StartupOptions options)
    {
        var (tested, overrides) = await Task.Run(() => (_service.GetTestedVersions(), _service.GetOverridesInWindowsEnvironment()));
        _testedVersions = tested;

        _environmentNotices.Clear();
        if (overrides is null)
        {
            _environmentNotices.Add("No se pudo obtener el entorno por defecto de Windows: por seguridad no se podrá abrir ningún perfil.");
        }
        else if (overrides.Count > 0)
        {
            _environmentNotices.Add("Tu entorno de Windows define variables que anulan la suscripción: " +
                                    string.Join(", ", overrides) +
                                    ". Se quitan al abrir cada perfil (no se cambia nada en tu sistema).");
        }

        // Primer arranque: la lista empieza con el Claude de siempre, sin tener que usar la línea de comandos.
        try
        {
            if (await Task.Run(_service.CreateProfilesFileIfMissing))
            {
                StatusMessage = "Bienvenido: tu Claude de siempre ya está en la lista. Usa «Nuevo perfil» para añadir otra cuenta.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProfileStoreException)
        {
            StatusMessage = "No se pudo crear la lista de perfiles: " + ex.Message;
        }

        await RefreshAsync();

        // Con los mensajes activados, el hook de cada perfil apunta a esta copia de cma.exe (puede haberse
        // instalado o movido) y los perfiles nuevos o renombrados quedan al día.
        try
        {
            if (await Task.Run(_service.SyncMessaging) is { Count: > 0 } problems)
            {
                StatusMessage = "Mensajes entre cuentas: " + problems[0];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProfileStoreException)
        {
            StatusMessage = "No se pudieron preparar los mensajes entre cuentas: " + ex.Message;
        }

        if (options.LaunchProfile is { } requested)
        {
            await LaunchRequestedAsync(requested);
        }
    }

    /// <summary>
    /// Abre el perfil pedido por id o nombre: desde <c>--launch</c> al arrancar o desde un acceso directo
    /// cuando la herramienta ya estaba abierta.
    /// </summary>
    public async Task LaunchRequestedAsync(string requested)
    {
        await RefreshAsync();
        var document = new ProfileDocument { Profiles = Profiles.Select(p => p.Profile).ToList() };
        var profile = document.Find(requested);
        var item = profile is null ? null : Profiles.FirstOrDefault(p => p.Id == profile.Id);
        if (item is null)
        {
            StatusMessage = $"No existe el perfil «{requested}» indicado con {StartupOptions.LaunchSwitch}.";
            return;
        }
        await OpenAsync(item);
    }

    /// <summary>Consulta el estado periódicamente hasta que se cancele (al cerrar la ventana).</summary>
    public async Task RunAutoRefreshAsync(CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                await RefreshAsync();
                await CheckNewMessagesAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // La ventana se cerró.
        }
    }

    /// <summary>Avisa una vez de cada mensaje que un perfil deja a otro (aún sin entregar).</summary>
    public async Task CheckNewMessagesAsync()
    {
        try
        {
            // Primero se abren los chats nuevos que piden los mensajes; esos ya avisan de por sí.
            var opened = await Task.Run(() => _service.OpenChatsForNewMessages(_utcNow()));
            foreach (var chat in opened.Where(c => _notifiedMessages.Add(c.MessageId)))
            {
                MessageArrived?.Invoke(chat.Notice);
            }
            var unread = await Task.Run(_service.UnreadMessages);
            foreach (var message in unread.Where(m => _notifiedMessages.Add(m.Id)))
            {
                MessageArrived?.Invoke(
                    $"«{message.FromName}» dejó un mensaje para «{message.ToName}». Lo recibirá al escribirle en un chat de Code.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Se vuelve a mirar en la siguiente vuelta.
        }
    }

    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }
        _refreshing = true;
        try
        {
            var previous = _lastProfiles;
            var snapshot = await Task.Run(() => TakeSnapshot(previous));
            Apply(snapshot);
        }
        catch (Exception ex)
        {
            // Un fallo inesperado no debe parar la vigilancia: se reintenta en la siguiente vuelta.
            StatusMessage = "No se pudo actualizar el estado: " + ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    public async Task OpenAsync(ProfileItemViewModel item)
    {
        if (!item.CanOpen)
        {
            return;
        }
        // Ya abierto: solo se trae al frente (sin aviso de carpetas ni asistente, que son para abrirlo).
        if (item.IsOpen)
        {
            await BringToFrontAsync(item);
            return;
        }
        // Dos Claude no deben trabajar a la vez en la misma carpeta.
        if (!await CheckFoldersAsync(item))
        {
            return;
        }
        // Primera vez de un perfil aislado: se abre con el asistente de inicio de sesión.
        if (item.Profile.NeedsLoginWizard)
        {
            await RunLoginWizardAsync(item);
            return;
        }
        await LaunchCoreAsync(item, recordLaunch: true);
    }

    private async Task BringToFrontAsync(ProfileItemViewModel item)
    {
        StatusMessage = $"Trayendo «{item.Name}» al frente…";
        try
        {
            var outcome = await Task.Run(() => _service.Launch(item.Profile));
            StatusMessage = outcome.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo traer «{item.Name}» al frente: {ex.Message}";
        }
        await RefreshAsync();
    }

    /// <summary>Lanza el perfil. Sin <paramref name="recordLaunch"/> no se apunta la fecha: así el asistente vuelve a salir si se abandona.</summary>
    private async Task<LaunchOutcome?> LaunchCoreAsync(ProfileItemViewModel item, bool recordLaunch)
    {
        var profile = item.Profile;
        LaunchOutcome? outcome = null;
        item.Pending = PendingOperation.Opening;
        _actionNotices.Clear();
        UpdateNotices();
        StatusMessage = $"Abriendo «{profile.Name}»…";
        try
        {
            outcome = await Task.Run(() => _service.Launch(profile));
            StatusMessage = outcome.Message;
            _actionNotices.AddRange(outcome.Warnings);

            if (recordLaunch && outcome.Kind == LaunchOutcomeKind.Launched)
            {
                var now = _utcNow();
                try
                {
                    await Task.Run(() => _service.RecordLaunch(profile.Id, now));
                }
                catch (Exception ex) when (ex is ProfileStoreException or IOException or UnauthorizedAccessException)
                {
                    _actionNotices.Add("Claude se abrió, pero no se pudo guardar la fecha de uso en profiles.json: " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            // La ventana no debe caerse por un fallo al lanzar; el núcleo ya registra los suyos.
            StatusMessage = $"No se pudo abrir «{profile.Name}»: {ex.Message}";
            outcome = new LaunchOutcome(LaunchOutcomeKind.Failed, StatusMessage, []);
        }
        finally
        {
            item.Pending = PendingOperation.None;
            UpdateNotices();
        }
        await RefreshAsync();
        return outcome;
    }

    private static bool IsActive(ProfileDisplayState state) => state is ProfileDisplayState.Running or ProfileDisplayState.Starting
        or ProfileDisplayState.RunningOutsideTool or ProfileDisplayState.Closing;

    /// <summary>True si se puede abrir: no hay conflicto, el usuario decide abrir igualmente o se creó una copia de trabajo.</summary>
    private async Task<bool> CheckFoldersAsync(ProfileItemViewModel item)
    {
        var profile = item.Profile;
        var active = Profiles.Where(p => p.Id != profile.Id && IsActive(p.State)).Select(p => p.Profile).ToList();
        // Si ya está abierto, «Abrir» solo lo trae al frente: no cambia nada sobre las carpetas.
        if (IsActive(item.State) || active.Count == 0 || profile.WorkFolders.Count == 0)
        {
            return true;
        }

        FolderConflictViewModel? warning;
        try
        {
            warning = await Task.Run(() =>
            {
                var conflicts = _service.FindFolderConflicts(profile, active);
                if (conflicts.Count == 0)
                {
                    return null;
                }
                var shared = conflicts.Select(c => c.Folder).Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(f => f, _service.FindSharedProjectFiles, StringComparer.OrdinalIgnoreCase);
                return new FolderConflictViewModel(profile.Name, conflicts, shared, _service.ProposeWorktree(profile, conflicts[0].Folder));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Si no se pueden comprobar las carpetas no se bloquea la apertura.
            return true;
        }
        if (warning is null)
        {
            return true;
        }

        switch (_dialogs.ResolveFolderConflict(warning))
        {
            case FolderConflictChoice.OpenAnyway:
                return true;
            case FolderConflictChoice.CreateWorktree when warning.Worktree is { } proposal:
                try
                {
                    await Task.Run(() => _service.CreateWorktree(profile.Id, proposal));
                    StatusMessage = $"Copia de trabajo creada en {proposal.WorktreePath} (rama {proposal.Branch}). " +
                                    $"En la pestaña Code de «{profile.Name}» elige esa carpeta.";
                    await RefreshAsync();
                    return true;
                }
                catch (Exception ex) when (ex is IOException or ProfileStoreException or UnauthorizedAccessException)
                {
                    StatusMessage = "No se creó la copia de trabajo: " + ex.Message;
                    return false;
                }
            default:
                StatusMessage = $"No se abrió «{profile.Name}».";
                return false;
        }
    }

    /// <summary>Avisos del editor para una carpeta de trabajo: archivos del proyecto compartidos y otros perfiles que la usan.</summary>
    internal IReadOnlyList<string> FolderNotes(Guid editingProfileId, string folder)
    {
        var notes = new List<string>();
        try
        {
            var shared = _service.FindSharedProjectFiles(folder);
            if (shared.Count > 0)
            {
                notes.Add($"{folder} tiene {string.Join(", ", shared)}: sus instrucciones y permisos valen para todas las cuentas que la abran.");
            }
            var probe = new Profile { Id = Guid.NewGuid(), Name = "", WorkFolders = [folder] };
            var others = Profiles.Where(p => p.Id != editingProfileId).Select(p => p.Profile);
            foreach (var conflict in _service.FindFolderConflicts(probe, others))
            {
                notes.Add($"{folder}: también la usa «{conflict.Other.Name}». No los tengas abiertos a la vez en esa carpeta; al abrir te propondré una copia de trabajo de Git.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Son solo avisos.
        }
        return notes;
    }

    private async Task RunLoginWizardAsync(ProfileItemViewModel item)
    {
        var profile = item.Profile;
        var wizard = new LoginWizardViewModel(item, new LoginWizardHost(
            Others: () => Profiles.Where(p => p.Id != profile.Id).ToList(),
            CloseOther: CloseAsync,
            Launch: () => LaunchCoreAsync(item, recordLaunch: false),
            Confirm: email => ConfirmLoginAsync(profile, email)));

        StatusMessage = $"Primer inicio de sesión de «{profile.Name}».";
        StatusMessage = _dialogs.RunLoginWizard(wizard)
            ? $"«{profile.Name}» ya tiene su cuenta. Puedes volver a abrir los demás perfiles."
            : $"No se terminó el inicio de sesión de «{profile.Name}». El asistente volverá a salir la próxima vez que lo abras.";
        await RefreshAsync();
    }

    /// <summary>Null si se guardó; si no, el motivo para mostrarlo en el asistente.</summary>
    private async Task<string?> ConfirmLoginAsync(Profile profile, string email)
    {
        var now = _utcNow();
        try
        {
            await Task.Run(() => _service.ConfirmLogin(profile.Id, email, now));
            return null;
        }
        catch (Exception ex) when (ex is ProfileStoreException or IOException or UnauthorizedAccessException)
        {
            return "No se pudo guardar la confirmación en profiles.json: " + ex.Message;
        }
    }

    public async Task CloseAsync(ProfileItemViewModel item)
    {
        if (!item.CanClose)
        {
            return;
        }

        var profile = item.Profile;
        if (!_dialogs.ConfirmDangerous($"Cerrar «{profile.Name}»", BuildCloseConfirmation(profile.Name)))
        {
            StatusMessage = $"No se cerró «{profile.Name}».";
            return;
        }

        item.Pending = PendingOperation.Closing;
        StatusMessage = $"Cerrando «{profile.Name}»…";
        try
        {
            // El usuario ya aceptó forzar: si el cierre amable no basta (va a la bandeja), se terminan sus procesos.
            var outcome = await Task.Run(() => _service.Close(profile, force: true, GracefulCloseTimeout));
            StatusMessage = outcome.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo cerrar «{profile.Name}»: {ex.Message}";
        }
        finally
        {
            item.Pending = PendingOperation.None;
        }
        await RefreshAsync();
    }

    public async Task NewProfileAsync()
    {
        var editor = new ProfileEditorViewModel(null, Profiles.Select(p => p.Name),
            _service.PreviewNewProfileFolders, _dialogs.PickFolder, folder => FolderNotes(Guid.Empty, folder));
        if (!_dialogs.EditProfile(editor) || editor.Result is not { } edit)
        {
            return;
        }

        try
        {
            var profile = await Task.Run(() => _service.CreateProfile(edit));
            StatusMessage = $"Perfil «{profile.Name}» creado. La primera vez que lo abras, inicia sesión con su cuenta " +
                            "(mejor con los demás Claude cerrados, para que el inicio de sesión no vaya a otra ventana).";
        }
        catch (Exception ex) when (ex is ProfileStoreException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = "No se creó el perfil: " + ex.Message;
        }
        await RefreshAsync();
    }

    public async Task EditAsync(ProfileItemViewModel item)
    {
        if (!item.CanEdit)
        {
            return;
        }
        var profile = item.Profile;
        var editor = new ProfileEditorViewModel(profile, Profiles.Where(p => p.Id != profile.Id).Select(p => p.Name),
            _service.PreviewNewProfileFolders, _dialogs.PickFolder, folder => FolderNotes(profile.Id, folder));
        if (!_dialogs.EditProfile(editor) || editor.Result is not { } edit)
        {
            return;
        }

        try
        {
            await Task.Run(() => _service.UpdateProfile(profile.Id, edit));
            StatusMessage = $"Cambios guardados en «{edit.Name}».";
        }
        catch (Exception ex) when (ex is ProfileStoreException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = "No se guardaron los cambios: " + ex.Message;
        }
        await RefreshAsync();
    }

    public async Task RemoveAsync(ProfileItemViewModel item)
    {
        if (!item.CanRemove)
        {
            return;
        }
        var profile = item.Profile;
        if (!_dialogs.ConfirmDangerous($"Quitar «{profile.Name}»", BuildRemoveConfirmation(profile)))
        {
            return;
        }

        try
        {
            await Task.Run(() => _service.RemoveProfile(profile.Id));
            StatusMessage = $"«{profile.Name}» ya no está en la lista. Sus carpetas siguen en tu PC.";
        }
        catch (Exception ex) when (ex is ProfileStoreException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"No se quitó «{profile.Name}»: " + ex.Message;
        }
        await RefreshAsync();
    }

    public async Task CreateShortcutAsync(ProfileItemViewModel item)
    {
        if (!item.CanEdit)
        {
            return;
        }
        var profile = item.Profile;
        if (_dialogs.ChooseShortcutLocation(profile.Name) is not { } location)
        {
            return;
        }

        try
        {
            var path = await Task.Run(() => _service.CreateShortcut(profile.Id, location));
            StatusMessage = $"Acceso directo creado: {path}. Abre «{profile.Name}» aunque la herramienta esté cerrada.";
        }
        catch (Exception ex) when (ex is ProfileStoreException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = "No se creó el acceso directo: " + ex.Message;
        }
        await RefreshAsync();
    }

    internal static string BuildRemoveConfirmation(Profile profile) =>
        $"«{profile.Name}» desaparecerá de la lista de perfiles" +
        (profile.Shortcuts.Count > 0 ? $" y se borrarán sus accesos directos ({profile.Shortcuts.Count})." : ".") +
        $"{Environment.NewLine}{Environment.NewLine}" +
        "No se borra nada: sus carpetas, su sesión y sus chats de Code se quedan en tu PC:" + Environment.NewLine +
        $"   {profile.DesktopDataDir}{Environment.NewLine}   {profile.ClaudeConfigDir}{Environment.NewLine}{Environment.NewLine}" +
        "Si creas otro perfil con el mismo nombre, tendrá carpetas nuevas y vacías." +
        $"{Environment.NewLine}{Environment.NewLine}¿Quitar «{profile.Name}» de la lista?";

    /// <summary>
    /// Entradas de perfiles para el menú del icono de la bandeja, con el estado actual. Las acciones son las
    /// mismas que los botones de la ventana (Cerrar… también pide confirmación).
    /// </summary>
    public IReadOnlyList<TrayMenuEntry> BuildTrayMenu()
    {
        if (Profiles.Count == 0)
        {
            return [new TrayMenuEntry("No hay perfiles", Enabled: false, Action: null)];
        }

        var entries = new List<TrayMenuEntry>();
        foreach (var item in Profiles)
        {
            // Mismas palabras que la tarjeta, y a través del comando: no se lanza dos veces la misma acción.
            entries.Add(new TrayMenuEntry($"{item.OpenLabel} «{item.Name}» · {item.ShortStateLabel}", item.CanOpen, () => item.OpenCommand.ExecuteAsync()));
            if (item.CanClose)
            {
                entries.Add(new TrayMenuEntry($"Cerrar «{item.Name}»…", Enabled: true, () => item.CloseCommand.ExecuteAsync()));
            }
        }
        return entries;
    }

    internal static string BuildCloseConfirmation(string profileName) =>
        $"Cerrar la ventana de Claude no lo cierra: Claude se queda en la bandeja del sistema.{Environment.NewLine}{Environment.NewLine}" +
        $"Para cerrar «{profileName}» desde aquí hay que forzar el cierre de sus procesos. " +
        $"Se perderá lo que no esté guardado (por ejemplo, una respuesta a medias o una sesión de Code en curso).{Environment.NewLine}{Environment.NewLine}" +
        "Alternativa sin forzar: en la bandeja del sistema, haz clic derecho en el icono de Claude de este perfil " +
        $"(hay uno por cada Claude abierto) y elige «Salir».{Environment.NewLine}{Environment.NewLine}" +
        $"¿Forzar el cierre de «{profileName}»?";

    /// <param name="Transient">Fallo de lectura pasajero (p. ej. state.json justo mientras se reescribe).</param>
    private sealed record ProfileSnapshot(Profile Profile, ProfileStatus? Status, string? Error, bool Transient = false);

    private sealed record Snapshot(
        LocateResult Claude,
        UpdateStatus? Update,
        ProfileDocument? Document,
        string? ProfilesProblem,
        bool ProfilesFileMissing,
        IReadOnlyList<ProfileSnapshot> Profiles);

    /// <summary>Se ejecuta fuera del hilo de la interfaz.</summary>
    private Snapshot TakeSnapshot(IReadOnlyList<Profile> previousProfiles)
    {
        var claude = _service.LocateClaude();

        ProfileDocument? document = null;
        string? problem = null;
        var missing = !_service.ProfilesFileExists;
        if (!missing)
        {
            try
            {
                document = _service.LoadProfiles();
            }
            catch (Exception ex) when (ex is ProfileStoreException or IOException or UnauthorizedAccessException)
            {
                // Se sigue mostrando la última lista buena.
                problem = ex.Message;
            }
        }

        IReadOnlyList<Profile> profiles = document is not null ? document.Profiles : missing ? [] : previousProfiles;
        var results = new List<ProfileSnapshot>(profiles.Count);
        foreach (var profile in profiles)
        {
            try
            {
                results.Add(new ProfileSnapshot(profile, _service.GetStatus(profile, claude.Install), null));
            }
            catch (IOException ex)
            {
                results.Add(new ProfileSnapshot(profile, null, ex.Message, Transient: true));
            }
            catch (Exception ex)
            {
                // Un perfil con problemas no debe impedir ver los demás.
                results.Add(new ProfileSnapshot(profile, null, ex.Message));
            }
        }
        UpdateStatus? update = null;
        if (claude.Install is { } install)
        {
            try
            {
                update = _service.GetUpdateStatus(install);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Sin el estado de versiones se usa solo la lista de versiones probadas.
            }
        }
        return new Snapshot(claude, update, document, problem, missing, results);
    }

    private void Apply(Snapshot snapshot)
    {
        _lastProfiles = snapshot.Profiles.Select(p => p.Profile).ToList();

        SyncProfiles(snapshot.Profiles);

        _snapshotNotices.Clear();
        _lastInstall = snapshot.Claude.Install;
        UpdateBanner = null;
        if (snapshot.Claude.Install is { } install)
        {
            ClaudeSummary = $"Claude Desktop {install.Version}";
            if (_testedVersions is null)
            {
                _snapshotNotices.Add("No se pudo leer la lista de versiones probadas de Claude Desktop.");
            }
            else if (snapshot.Update is { } update)
            {
                ApplyUpdateStatus(update);
            }
            else if (!_testedVersions.IsTested(install.Version))
            {
                UpdateBanner = UntestedText(install.Version, null) + CheckAdvice;
            }
        }
        else
        {
            ClaudeSummary = "Claude Desktop no encontrado";
            _snapshotNotices.Add("No se encontró Claude Desktop: no se puede abrir ningún perfil. " + snapshot.Claude.Error);
        }

        if (snapshot.ProfilesProblem is not null)
        {
            _snapshotNotices.Add(snapshot.ProfilesProblem);
        }

        EmptyMessage = Profiles.Count > 0
            ? null
            : snapshot.ProfilesFileMissing
                ? $"No se pudo crear la lista de perfiles ({_service.ProfilesFileDisplayPath}). Comprueba que puedes escribir en esa carpeta y vuelve a abrir la herramienta."
                : "profiles.json no tiene perfiles.";

        UpdateNotices();
    }

    private const string CheckAdvice = " Comprueba que tus perfiles siguen bien antes de usarlos.";

    private static string UntestedText(Version installed, Version? newestTested) =>
        $"Claude Desktop {installed} no está en la lista de versiones probadas" +
        (newestTested is null ? "" : $" (la más nueva probada es {newestTested})") + ".";

    /// <summary>Aviso de actualización: versión nueva sin comprobar, versión no probada o ya comprobada aquí.</summary>
    private void ApplyUpdateStatus(UpdateStatus update)
    {
        if (update.ChangedSinceKnown)
        {
            UpdateBanner = $"Claude Desktop se actualizó de {update.Previous} a {update.Installed}. " +
                           (update.IsTested ? "Esta versión está en la lista de versiones probadas." : UntestedText(update.Installed, update.NewestTested)) +
                           CheckAdvice;
        }
        else if (!update.IsTested && !update.CheckedOnThisPc)
        {
            UpdateBanner = UntestedText(update.Installed, update.NewestTested) + CheckAdvice;
        }
        else if (!update.IsTested && update.CheckedUtc is { } checkedUtc)
        {
            _snapshotNotices.Add($"Claude Desktop {update.Installed} no está en la lista de versiones probadas del proyecto, " +
                                 $"pero la comprobación de este equipo fue bien el {checkedUtc.ToLocalTime():dd/MM/yyyy}.");
        }
    }

    /// <summary>Actualiza la lista sin recrear las filas, para no perder la selección ni los botones.</summary>
    private void SyncProfiles(IReadOnlyList<ProfileSnapshot> snapshots)
    {
        for (var i = Profiles.Count - 1; i >= 0; i--)
        {
            if (snapshots.All(s => s.Profile.Id != Profiles[i].Id))
            {
                Profiles.RemoveAt(i);
            }
        }

        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            var item = Profiles.FirstOrDefault(p => p.Id == snapshot.Profile.Id);
            if (item is null)
            {
                item = new ProfileItemViewModel(snapshot.Profile, _actions);
                Profiles.Insert(index, item);
            }
            else
            {
                item.Profile = snapshot.Profile;
                var current = Profiles.IndexOf(item);
                if (current != index)
                {
                    Profiles.Move(current, index);
                }
            }

            if (snapshot.Status is not null)
            {
                item.UpdateStatus(snapshot.Status);
            }
            else if (snapshot.Transient && item.HasStatus)
            {
                // Se conserva el último estado conocido en vez de mostrar "Error" durante una vuelta.
            }
            else
            {
                item.UpdateError(snapshot.Error ?? "error desconocido");
            }
        }
    }

    private void UpdateNotices()
    {
        var wanted = _environmentNotices.Concat(_snapshotNotices).Concat(_actionNotices).Distinct().ToList();
        if (wanted.SequenceEqual(Notices))
        {
            return;
        }
        Notices.Clear();
        foreach (var notice in wanted)
        {
            Notices.Add(notice);
        }
    }
}

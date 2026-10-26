using System.Text.RegularExpressions;
using ClaudeMultiAccount.Core.Processes;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.App.ViewModels;

public enum ProfileDisplayState
{
    /// <summary>Todavía no se ha comprobado (antes de la primera consulta).</summary>
    Unknown,
    Closed,
    Starting,
    Running,
    RunningOutsideTool,
    Closing,
    Error,
}

/// <summary>Operación que la ventana tiene en curso sobre el perfil (se muestra encima del estado real).</summary>
public enum PendingOperation
{
    None,
    Opening,
    Closing,
}

/// <summary>Lo que hacen los botones de una fila (los implementa la ventana principal).</summary>
public sealed record ProfileActions(
    Func<ProfileItemViewModel, Task> Open,
    Func<ProfileItemViewModel, Task> Close,
    Func<ProfileItemViewModel, Task> Edit,
    Func<ProfileItemViewModel, Task> Remove,
    Func<ProfileItemViewModel, Task> CreateShortcut);

/// <summary>Una fila de la lista: el perfil, su estado y sus botones.</summary>
public sealed partial class ProfileItemViewModel : ObservableObject
{
    public const string DefaultColor = "#D97757";

    private Profile _profile;
    private ProfileStatus? _status;
    private string? _statusError;
    private PendingOperation _pending;

    public ProfileItemViewModel(Profile profile, ProfileActions actions)
    {
        _profile = profile;
        OpenCommand = new AsyncRelayCommand(() => actions.Open(this), () => CanOpen);
        CloseCommand = new AsyncRelayCommand(() => actions.Close(this), () => CanClose);
        EditCommand = new AsyncRelayCommand(() => actions.Edit(this), () => CanEdit);
        RemoveCommand = new AsyncRelayCommand(() => actions.Remove(this), () => CanRemove);
        CreateShortcutCommand = new AsyncRelayCommand(() => actions.CreateShortcut(this), () => CanEdit);
    }

    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand CloseCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand RemoveCommand { get; }
    public AsyncRelayCommand CreateShortcutCommand { get; }

    public Profile Profile
    {
        get => _profile;
        set
        {
            if (SetProperty(ref _profile, value))
            {
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Initial));
                OnPropertyChanged(nameof(Color));
                OnPropertyChanged(nameof(TypeLabel));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(IsMain));
            }
        }
    }

    public Guid Id => _profile.Id;

    public string Name => _profile.Name;

    public string Initial
    {
        get
        {
            var source = string.IsNullOrWhiteSpace(_profile.IconGlyph) ? _profile.Name.Trim() : _profile.IconGlyph.Trim();
            return source.Length == 0 ? "?" : char.ToUpperInvariant(source[0]).ToString();
        }
    }

    /// <summary>Color en formato #RRGGBB o #AARRGGBB; si profiles.json trae otra cosa se usa el de por defecto.</summary>
    public string Color => HexColor().IsMatch(_profile.IconColor ?? "") ? _profile.IconColor! : DefaultColor;

    public bool IsMain => _profile.Type == ProfileType.Main;

    public string TypeLabel => _profile.Type switch
    {
        ProfileType.Main => "Principal",
        ProfileType.DesktopIsolated => "Desktop aislado",
        ProfileType.Cli => "Terminal",
        _ => "Desconocido",
    };

    /// <summary>Línea bajo el nombre en la tarjeta: qué es el perfil y, si lo hay, el correo de referencia.</summary>
    public string Description
    {
        get
        {
            var kind = _profile.Type switch
            {
                ProfileType.Main => "Perfil principal · tus datos de siempre",
                ProfileType.DesktopIsolated => "Perfil aislado · su propia cuenta",
                ProfileType.Cli => "Perfil de terminal",
                _ => "Tipo desconocido",
            };
            return string.IsNullOrWhiteSpace(_profile.EmailHint) ? kind : $"{kind} · {_profile.EmailHint.Trim()}";
        }
    }

    public PendingOperation Pending
    {
        get => _pending;
        set
        {
            if (SetProperty(ref _pending, value))
            {
                NotifyStateChanged();
            }
        }
    }

    public int ProcessCount => _status?.ProcessIds.Count ?? 0;

    /// <summary>Hay un estado comprobado (no "Comprobando…" ni error).</summary>
    public bool HasStatus => _status is not null;

    public ProfileDisplayState State => _pending switch
    {
        PendingOperation.Opening => ProfileDisplayState.Starting,
        PendingOperation.Closing => ProfileDisplayState.Closing,
        _ when _statusError is not null => ProfileDisplayState.Error,
        _ when _status is null => ProfileDisplayState.Unknown,
        _ => _status.State switch
        {
            ProfileState.Closed => ProfileDisplayState.Closed,
            ProfileState.Starting => ProfileDisplayState.Starting,
            ProfileState.Running when _status.OpenedOutsideTool => ProfileDisplayState.RunningOutsideTool,
            ProfileState.Running => ProfileDisplayState.Running,
            _ => ProfileDisplayState.Error,
        },
    };

    public string StateLabel => State switch
    {
        ProfileDisplayState.Unknown => "Comprobando…",
        ProfileDisplayState.Closed => "Cerrado",
        ProfileDisplayState.Starting => "Iniciando…",
        ProfileDisplayState.Running => "En ejecución",
        ProfileDisplayState.RunningOutsideTool => "En ejecución (abierto fuera de la herramienta)",
        ProfileDisplayState.Closing => "Cerrando…",
        _ => "Error",
    };

    /// <summary>Texto corto de la pastilla de estado; el detalle va en <see cref="StateDetail"/>.</summary>
    public string ShortStateLabel => State switch
    {
        ProfileDisplayState.Unknown => "Comprobando…",
        ProfileDisplayState.Closed => "Cerrado",
        ProfileDisplayState.Starting => "Abriendo…",
        ProfileDisplayState.Running => "Abierto",
        ProfileDisplayState.RunningOutsideTool => "Abierto fuera",
        ProfileDisplayState.Closing => "Cerrando…",
        _ => "Error",
    };

    /// <summary>Abierto (desde la herramienta o fuera de ella): el botón principal lo trae al frente.</summary>
    public bool IsOpen => State is ProfileDisplayState.Running or ProfileDisplayState.RunningOutsideTool;

    /// <summary>Lo contrario de <see cref="IsOpen"/>: la tarjeta muestra «Abrir» en color de acento.</summary>
    public bool IsClosed => !IsOpen;

    public string OpenLabel => IsOpen ? "Traer al frente" : "Abrir";

    public string OpenToolTip => IsOpen
        ? "Muestra la ventana de este Claude, aunque esté escondido junto al reloj."
        : "Abre este perfil con su propia cuenta.";

    /// <summary>Explicación del estado para la información sobre herramientas.</summary>
    public string StateDetail => State switch
    {
        ProfileDisplayState.RunningOutsideTool =>
            "Este perfil se abrió sin esta herramienta (por ejemplo, desde el menú Inicio). Se ve aquí, pero no se puede cerrar desde aquí.",
        ProfileDisplayState.Error when _statusError is not null => "No se pudo comprobar el estado: " + _statusError,
        ProfileDisplayState.Error => "No se pudo comprobar si este perfil está abierto (no se pudo leer su archivo de bloqueo).",
        ProfileDisplayState.Running or ProfileDisplayState.Starting when ProcessCount > 0 =>
            $"Abierto desde esta herramienta · {ProcessCount} proceso(s).",
        _ => StateLabel,
    };

    public bool CanOpen => _pending == PendingOperation.None && State != ProfileDisplayState.Unknown;

    /// <summary>Solo se cierra lo que abrió la herramienta (hay árbol de procesos conocido).</summary>
    public bool CanClose => _pending == PendingOperation.None && ProcessCount > 0;

    public bool CanEdit => _pending == PendingOperation.None;

    /// <summary>Solo un perfil aislado y cerrado: así no queda ningún Claude abierto sin perfil que lo vigile.</summary>
    public bool CanRemove => !IsMain && _pending == PendingOperation.None
                             && State is (ProfileDisplayState.Closed or ProfileDisplayState.Error) && ProcessCount == 0;

    public string RemoveToolTip => CanRemove
        ? "Quita el perfil de la lista. Sus carpetas y su sesión se quedan en tu PC."
        : "Cierra el perfil antes de quitarlo de la lista.";

    public string CloseToolTip => State == ProfileDisplayState.RunningOutsideTool
        ? "Se abrió fuera de esta herramienta: ciérralo desde Claude, con «Salir» en su icono de la bandeja del sistema."
        : "Fuerza el cierre de este perfil (pide confirmación).";

    public void UpdateStatus(ProfileStatus status)
    {
        _status = status;
        _statusError = null;
        NotifyStateChanged();
    }

    public void UpdateError(string message)
    {
        _status = null;
        _statusError = message;
        NotifyStateChanged();
    }

    /// <summary>Lo que lee un lector de pantalla para la fila (si no, leería el nombre de la clase).</summary>
    public override string ToString() => $"{Name}, {StateLabel}";

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(StateDetail));
        OnPropertyChanged(nameof(ShortStateLabel));
        OnPropertyChanged(nameof(OpenLabel));
        OnPropertyChanged(nameof(OpenToolTip));
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(ProcessCount));
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(CloseToolTip));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(RemoveToolTip));
        OpenCommand.RaiseCanExecuteChanged();
        CloseCommand.RaiseCanExecuteChanged();
        EditCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
        CreateShortcutCommand.RaiseCanExecuteChanged();
    }

    [GeneratedRegex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColor();
}

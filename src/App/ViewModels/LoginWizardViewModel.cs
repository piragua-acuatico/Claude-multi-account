using System.Collections.ObjectModel;
using ClaudeMultiAccount.Core.Launch;

namespace ClaudeMultiAccount.App.ViewModels;

public enum LoginWizardStep
{
    CloseOthers,
    SignIn,
    Verify,
}

/// <summary>Lo que el asistente necesita de la ventana principal.</summary>
public sealed record LoginWizardHost(
    Func<IEnumerable<ProfileItemViewModel>> Others,
    Func<ProfileItemViewModel, Task> CloseOther,
    Func<Task<LaunchOutcome?>> Launch,
    Func<string, Task<string?>> Confirm);

/// <summary>
/// Primer inicio de sesión de un perfil aislado (procedimiento comprobado con dos cuentas reales): con los demás Claude cerrados, se abre solo este perfil, el usuario inicia sesión en su
/// navegador y confirma con sus ojos qué correo muestra Claude. La herramienta no ve ni guarda nada de la
/// sesión: solo apunta que el usuario lo confirmó (y el correo, si lo escribe, como recordatorio).
/// </summary>
public sealed class LoginWizardViewModel : ObservableObject
{
    private readonly ProfileItemViewModel _target;
    private readonly LoginWizardHost _host;
    private LoginWizardStep _step = LoginWizardStep.CloseOthers;
    private bool _launched;
    private string? _launchMessage;
    private string _email;
    private bool _accountIsCorrect;
    private bool _accountIsWrong;
    private string? _error;
    private bool _busy;

    public LoginWizardViewModel(ProfileItemViewModel target, LoginWizardHost host)
    {
        _target = target;
        _host = host;
        _email = target.Profile.EmailHint;

        NextCommand = new AsyncRelayCommand(NextAsync, () => CanGoNext);
        BackCommand = new RelayCommand(Back, () => _step != LoginWizardStep.CloseOthers && !_busy);
        CloseOthersCommand = new AsyncRelayCommand(CloseOthersAsync, () => ClosableOthers.Any() && !_busy);
        OpenProfileCommand = new AsyncRelayCommand(OpenProfileAsync, () => !_busy);
        UpdateOthers();
    }

    public event EventHandler? Completed;

    /// <summary>Guardando o abriendo: la ventana no se deja cerrar a medias.</summary>
    public bool IsBusy => _busy;

    public string ProfileName => _target.Name;

    public string Title => $"Iniciar sesión en «{_target.Name}»";

    public LoginWizardStep Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, value))
            {
                OnPropertyChanged(nameof(StepText));
                OnPropertyChanged(nameof(NextLabel));
                OnPropertyChanged(nameof(Step1State));
                OnPropertyChanged(nameof(Step2State));
                OnPropertyChanged(nameof(Step3State));
                RaiseCommands();
            }
        }
    }

    public string StepText => $"Paso {(int)_step + 1} de 3";

    /// <summary>Para el indicador de pasos: «Done», «Current» o «Pending».</summary>
    public string Step1State => StateOf(LoginWizardStep.CloseOthers);

    public string Step2State => StateOf(LoginWizardStep.SignIn);

    public string Step3State => StateOf(LoginWizardStep.Verify);

    private string StateOf(LoginWizardStep step) => step < _step ? "Done" : step == _step ? "Current" : "Pending";

    public string NextLabel => _step == LoginWizardStep.Verify ? "Terminar" : "Siguiente";

    /// <summary>Claude abiertos que hay que cerrar antes de iniciar sesión, con lo que puede hacer el usuario.</summary>
    public ObservableCollection<string> OpenOthers { get; } = [];

    public bool AllOthersClosed => OpenOthers.Count == 0;

    public AsyncRelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public AsyncRelayCommand CloseOthersCommand { get; }
    public AsyncRelayCommand OpenProfileCommand { get; }

    public bool Launched
    {
        get => _launched;
        private set => SetProperty(ref _launched, value);
    }

    public string? LaunchMessage
    {
        get => _launchMessage;
        private set => SetProperty(ref _launchMessage, value);
    }

    /// <summary>Correo que el usuario ve en Ajustes de Claude; opcional, solo para recordarle qué cuenta es.</summary>
    public string Email
    {
        get => _email;
        set
        {
            if (SetProperty(ref _email, value))
            {
                OnPropertyChanged(nameof(DuplicateWarning));
                RaiseCommands();
            }
        }
    }

    public bool AccountIsCorrect
    {
        get => _accountIsCorrect;
        set
        {
            if (SetProperty(ref _accountIsCorrect, value))
            {
                if (value)
                {
                    AccountIsWrong = false;
                }
                RaiseCommands();
            }
        }
    }

    public bool AccountIsWrong
    {
        get => _accountIsWrong;
        set
        {
            if (SetProperty(ref _accountIsWrong, value))
            {
                if (value)
                {
                    AccountIsCorrect = false;
                }
                RaiseCommands();
            }
        }
    }

    /// <summary>Si el correo escrito es el de referencia de otro perfil: casi seguro se entró con la cuenta equivocada.</summary>
    public string? DuplicateWarning
    {
        get
        {
            var email = _email.Trim();
            if (email.Length == 0)
            {
                return null;
            }
            var owner = _host.Others().FirstOrDefault(p =>
                string.Equals(p.Profile.EmailHint.Trim(), email, StringComparison.OrdinalIgnoreCase));
            return owner is null
                ? null
                : $"Ese es el correo de «{owner.Name}». En esta ventana cierra sesión y entra con la cuenta de «{_target.Name}».";
        }
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public bool CanGoNext => !_busy && _step switch
    {
        LoginWizardStep.CloseOthers => AllOthersClosed,
        LoginWizardStep.SignIn => Launched,
        _ => AccountIsCorrect && DuplicateWarning is null,
    };

    /// <summary>Se llama cada segundo desde la ventana: el usuario puede cerrar Claude por su cuenta.</summary>
    public void UpdateOthers()
    {
        var wanted = OpenOthersNow().Select(Describe).ToList();
        if (!wanted.SequenceEqual(OpenOthers))
        {
            OpenOthers.Clear();
            foreach (var line in wanted)
            {
                OpenOthers.Add(line);
            }
            OnPropertyChanged(nameof(AllOthersClosed));
        }
        RaiseCommands();
    }

    private IEnumerable<ProfileItemViewModel> OpenOthersNow() =>
        _host.Others().Where(p => p.State is ProfileDisplayState.Running or ProfileDisplayState.Starting
            or ProfileDisplayState.RunningOutsideTool or ProfileDisplayState.Closing);

    private IEnumerable<ProfileItemViewModel> ClosableOthers => OpenOthersNow().Where(p => p.CanClose);

    private static string Describe(ProfileItemViewModel p) => p.CanClose
        ? $"«{p.Name}»: abierto desde la herramienta (se puede cerrar con el botón de abajo)."
        : $"«{p.Name}»: abierto fuera de la herramienta. Sal de él desde su icono de la bandeja («Salir»).";

    private async Task CloseOthersAsync()
    {
        _busy = true;
        RaiseCommands();
        try
        {
            foreach (var item in ClosableOthers.ToList())
            {
                await _host.CloseOther(item);
            }
        }
        finally
        {
            _busy = false;
            UpdateOthers();
        }
    }

    private async Task OpenProfileAsync()
    {
        _busy = true;
        Error = null;
        RaiseCommands();
        try
        {
            var outcome = await _host.Launch();
            LaunchMessage = outcome?.Message;
            Launched = outcome is { Kind: LaunchOutcomeKind.Launched or LaunchOutcomeKind.AlreadyRunning };
            if (outcome is { Kind: LaunchOutcomeKind.Failed })
            {
                Error = outcome.Message;
            }
        }
        finally
        {
            _busy = false;
            RaiseCommands();
        }
    }

    private async Task NextAsync()
    {
        switch (_step)
        {
            case LoginWizardStep.CloseOthers:
                UpdateOthers();
                if (AllOthersClosed)
                {
                    Step = LoginWizardStep.SignIn;
                }
                break;
            case LoginWizardStep.SignIn:
                Step = LoginWizardStep.Verify;
                break;
            default:
                _busy = true;
                RaiseCommands();
                try
                {
                    Error = await _host.Confirm(_email.Trim());
                }
                finally
                {
                    _busy = false;
                    RaiseCommands();
                }
                if (Error is null)
                {
                    Completed?.Invoke(this, EventArgs.Empty);
                }
                break;
        }
    }

    private void Back()
    {
        Error = null;
        // Si la cuenta no era la correcta se vuelve al paso de iniciar sesión (tras cerrar sesión en Claude).
        Step = _step == LoginWizardStep.Verify ? LoginWizardStep.SignIn : LoginWizardStep.CloseOthers;
        AccountIsCorrect = false;
        AccountIsWrong = false;
    }

    private void RaiseCommands()
    {
        OnPropertyChanged(nameof(CanGoNext));
        NextCommand.RaiseCanExecuteChanged();
        BackCommand.RaiseCanExecuteChanged();
        CloseOthersCommand.RaiseCanExecuteChanged();
        OpenProfileCommand.RaiseCanExecuteChanged();
    }
}

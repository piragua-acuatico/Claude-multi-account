using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.Core.Messaging;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.App.ViewModels;

/// <summary>Un mensaje del historial, listo para mostrar.</summary>
public sealed record MessageRow(string Direction, string When, string Text, bool Unread)
{
    public static MessageRow From(MailboxEntry entry, DateTime nowLocal)
    {
        var message = entry.Message;
        var local = message.SentUtc.ToLocalTime();
        var when = local.Date == nowLocal.Date
            ? "hoy, " + local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);
        return new MessageRow($"{message.FromName}  →  {message.ToName}", when, message.Text, entry.Unread);
    }
}

/// <summary>
/// Ventana «Mensajes entre cuentas»: activarlos o desactivarlos y ver lo que se han pasado los perfiles.
/// </summary>
public sealed class MessagesViewModel : ObservableObject
{
    private const int HistorySize = 100;

    private readonly IProfileService _service;
    private bool _isEnabled;
    private bool _busy;
    private string? _message;

    public MessagesViewModel(IProfileService service)
    {
        _service = service;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => !_busy);
    }

    public AsyncRelayCommand ToggleCommand { get; }

    public ObservableCollection<MessageRow> Messages { get; } = [];

    /// <summary>Perfiles cuyo settings.json no se pudo preparar, por ejemplo.</summary>
    public ObservableCollection<string> Problems { get; } = [];

    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(IsDisabled));
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    public bool IsDisabled => !IsEnabled;

    public string StateText => IsEnabled
        ? "Activados: cada Claude puede enviar mensajes a tus otras cuentas cuando se lo pidas."
        : "Desactivados. Al activarlos se añade a cada perfil una skill y un aviso automático de Claude Code.";

    public bool HasMessages => Messages.Count > 0;

    public bool HasNoMessages => !HasMessages;

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public async Task LoadAsync()
    {
        try
        {
            var (enabled, history) = await Task.Run(() => (_service.MessagingEnabled, _service.MessageHistory(HistorySize)));
            IsEnabled = enabled;
            var now = DateTime.Now;
            Messages.Clear();
            foreach (var entry in history)
            {
                Messages.Add(MessageRow.From(entry, now));
            }
            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(HasNoMessages));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProfileStoreException)
        {
            Message = "No se pudieron leer los mensajes: " + ex.Message;
        }
    }

    private async Task ToggleAsync()
    {
        _busy = true;
        ToggleCommand.RaiseCanExecuteChanged();
        var enable = !IsEnabled;
        try
        {
            var problems = await Task.Run(() => _service.SetMessaging(enable));
            Problems.Clear();
            foreach (var problem in problems)
            {
                Problems.Add(problem);
            }
            IsEnabled = await Task.Run(() => _service.MessagingEnabled);
            Message = (enable, problems.Count) switch
            {
                (true, 0) => "Listo. Abre un chat de Code nuevo en cada perfil para que lo use.",
                (true, _) => "Activados, pero algún perfil no se pudo preparar: mira el aviso.",
                (false, 0) => "Desactivados: se quitó la skill y el aviso automático de cada perfil. El historial se conserva.",
                (false, _) => "Desactivados, pero algún perfil no se pudo limpiar: mira el aviso.",
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProfileStoreException)
        {
            Message = "No se pudo cambiar: " + ex.Message;
        }
        finally
        {
            _busy = false;
            ToggleCommand.RaiseCanExecuteChanged();
        }
    }
}

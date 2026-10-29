using System.Windows;
using ClaudeMultiAccount.App.ViewModels;
using ClaudeMultiAccount.Core.Shortcuts;
using Microsoft.Win32;

namespace ClaudeMultiAccount.App.Services;

public interface IDialogService
{
    /// <summary>Pregunta Sí/No con "No" como respuesta por defecto. True solo si el usuario elige "Sí".</summary>
    bool ConfirmDangerous(string title, string message);

    /// <summary>Muestra el editor de perfil. True si el usuario guardó (y el formulario era válido).</summary>
    bool EditProfile(ProfileEditorViewModel editor);

    /// <summary>Selector de carpetas; null si el usuario cancela.</summary>
    string? PickFolder();

    /// <summary>Dónde crear el acceso directo del perfil; null si el usuario cancela.</summary>
    ShortcutLocation? ChooseShortcutLocation(string profileName);

    /// <summary>Muestra el asistente de inicio de sesión. True si el usuario lo terminó (confirmó la cuenta).</summary>
    bool RunLoginWizard(LoginWizardViewModel wizard);

    /// <summary>Muestra el panel "Qué comparte cada perfil".</summary>
    void ShowSharingInfo(SharingInfoViewModel info);

    /// <summary>Aviso de carpeta en uso por otro perfil abierto: qué hace el usuario.</summary>
    FolderConflictChoice ResolveFolderConflict(FolderConflictViewModel warning);

    /// <summary>Resultado de la comprobación de compatibilidad.</summary>
    void ShowCompatibilityReport(CompatibilityReportViewModel report);

    /// <summary>Ventana de copias de seguridad.</summary>
    void ShowBackups(BackupsViewModel backups);

    void ShowMessages(MessagesViewModel messages);
}

public sealed class MessageBoxDialogService : IDialogService
{
    public bool ConfirmDangerous(string title, string message) => MessageWindow.ConfirmWarning(title, message);

    public bool EditProfile(ProfileEditorViewModel editor)
    {
        var window = new ProfileEditorWindow(editor);
        return ModalWindow.Show(window) == true;
    }

    public ShortcutLocation? ChooseShortcutLocation(string profileName)
    {
        var window = new ShortcutLocationWindow(profileName);
        return ModalWindow.Show(window) == true ? window.Location : null;
    }

    public bool RunLoginWizard(LoginWizardViewModel wizard)
    {
        var window = new LoginWizardWindow(wizard);
        return ModalWindow.Show(window) == true;
    }

    public FolderConflictChoice ResolveFolderConflict(FolderConflictViewModel warning)
    {
        var window = new FolderConflictWindow(warning);
        return ModalWindow.Show(window) == true ? window.Choice : FolderConflictChoice.Cancel;
    }

    public void ShowCompatibilityReport(CompatibilityReportViewModel report) => _ = ModalWindow.Show(new CompatibilityReportWindow(report));

    public void ShowBackups(BackupsViewModel backups) => _ = ModalWindow.Show(new BackupsWindow(backups));

    public void ShowMessages(MessagesViewModel messages) => _ = ModalWindow.Show(new MessagesWindow(messages));

    public void ShowSharingInfo(SharingInfoViewModel info)
    {
        var window = new SharingInfoWindow(info);
        ModalWindow.Show(window);
    }

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Elige una carpeta de trabajo", Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}

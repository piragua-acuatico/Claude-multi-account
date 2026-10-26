using System.Windows;
using System.Windows.Threading;
using ClaudeMultiAccount.App.ViewModels;

namespace ClaudeMultiAccount.App;

public partial class LoginWizardWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public LoginWizardWindow(LoginWizardViewModel wizard)
    {
        InitializeComponent();
        DataContext = wizard;
        // El usuario puede cerrar los demás Claude por su cuenta (p. ej. «Salir» en la bandeja): se vuelve a mirar cada segundo.
        _timer.Tick += (_, _) => wizard.UpdateOthers();
        _timer.Start();
        wizard.Completed += (_, _) =>
        {
            // Si la ventana ya se cerró (p. ej. con Escape mientras guardaba), no se puede fijar el resultado.
            if (IsVisible)
            {
                DialogResult = true;
            }
        };
        // Mientras guarda la confirmación, cerrar dejaría el resultado a medias.
        Closing += (_, e) => e.Cancel = wizard.IsBusy;
        Closed += (_, _) => _timer.Stop();
    }
}

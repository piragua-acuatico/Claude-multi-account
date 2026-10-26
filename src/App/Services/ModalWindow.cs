using System.Windows;

namespace ClaudeMultiAccount.App.Services;

/// <summary>Cómo se muestran todos los diálogos de la herramienta.</summary>
internal static class ModalWindow
{
    /// <summary>
    /// Con la ventana principal visible, el diálogo se centra sobre ella. Si está escondida en la bandeja (p. ej. al
    /// abrir un perfil desde el icono), el diálogo sale centrado, encima de todo y con botón en la barra de tareas,
    /// para que no se quede escondido detrás de otras ventanas.
    /// </summary>
    public static bool? Show(Window window)
    {
        if (Application.Current?.MainWindow is { IsVisible: true } owner && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowInTaskbar = true;
            window.Topmost = true;
        }
        return window.ShowDialog();
    }
}

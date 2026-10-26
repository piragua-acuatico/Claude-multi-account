using System.Windows;
using System.Windows.Threading;

namespace ClaudeMultiAccount.App;

/// <summary>
/// Ninguna ventana de la herramienta se sale de la pantalla: si es más alta que el área de trabajo
/// (pantallas pequeñas o con mucho escalado) se recorta, y su contenido pasa a tener barra de desplazamiento.
/// </summary>
internal static class WindowFit
{
    private const double Margin = 16;

    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = (Window)sender;
        var area = SystemParameters.WorkArea;
        window.MaxHeight = Math.Min(window.MaxHeight, area.Height - 2 * Margin);
        // Después de que se aplique el nuevo tamaño, se vuelve a meter dentro del área de trabajo.
        window.Dispatcher.InvokeAsync(() =>
        {
            if (window.WindowState != WindowState.Normal)
                return;
            if (window.Top + window.ActualHeight > area.Bottom - Margin)
                window.Top = area.Bottom - Margin - window.ActualHeight;
            if (window.Top < area.Top + Margin)
                window.Top = area.Top + Margin;
        }, DispatcherPriority.Loaded);
    }
}

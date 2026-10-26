using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeMultiAccount.App.Tray;

/// <summary>
/// Icono de la herramienta para la bandeja y la ventana. Sale de Assets/app.ico, el mismo archivo que lleva
/// incrustado ClaudeMA.exe (barra de tareas y Explorador). Es el logo de la herramienta.
/// </summary>
internal sealed class AppIcon : IDisposable
{
    private static readonly Uri Source = new("pack://application:,,,/Assets/app.ico", UriKind.Absolute);

    public AppIcon()
    {
        using var stream = Application.GetResourceStream(Source)?.Stream
            ?? throw new InvalidOperationException("Falta el recurso Assets/app.ico.");
        // El tamaño pequeño del sistema es el que usa la bandeja (16 px a 100 % de escala).
        Icon = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    /// <summary>Para el NotifyIcon. Es válido mientras no se libere este objeto.</summary>
    public System.Drawing.Icon Icon { get; }

    /// <summary>El icono para la ventana de WPF (elige el tamaño adecuado de los que trae el .ico).</summary>
    public ImageSource ToImageSource() => BitmapFrame.Create(Source);

    public void Dispose() => Icon.Dispose();
}

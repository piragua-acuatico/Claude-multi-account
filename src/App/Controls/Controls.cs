using System.Windows;
using System.Windows.Controls;

namespace ClaudeMultiAccount.App.Controls;

/// <summary>
/// Fila de ajuste como en Configuración de Windows 11: icono, título y descripción a la izquierda y el control
/// (el contenido) a la derecha. Su plantilla es un estilo implícito en Themes/Controls.xaml.
/// </summary>
public class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(null));

    /// <summary>Carácter de Segoe Fluent Icons (p. ej. "&#xE713;"). Vacío: sin icono.</summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingsCard), new PropertyMetadata(null));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
}

public enum NoticeSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Aviso con icono, título y mensaje según su gravedad (como el InfoBar de WinUI). El contenido, si lo hay, son sus
/// acciones (botones) y va debajo del mensaje.
/// </summary>
public class Notice : ContentControl
{
    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(NoticeSeverity), typeof(Notice), new PropertyMetadata(NoticeSeverity.Informational));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Notice), new PropertyMetadata(null));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(Notice), new PropertyMetadata(null));

    public NoticeSeverity Severity { get => (NoticeSeverity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string? Message { get => (string?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
}

/// <summary>Opción grande y pulsable con icono, título y descripción (para elegir entre pocas opciones).</summary>
public class OptionCard : Button
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(OptionCard), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(OptionCard), new PropertyMetadata(null));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(OptionCard), new PropertyMetadata(null));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
}

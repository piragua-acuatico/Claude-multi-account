using System.Windows;
using ClaudeMultiAccount.App.Services;

namespace ClaudeMultiAccount.App;

/// <summary>
/// Confirmaciones y avisos propios, en español: el MessageBox de Windows pone sus botones en el idioma del
/// sistema («Yes / No» en un Windows en inglés). En las confirmaciones, «No» es la opción por defecto
/// (Intro y Escape responden «No»), porque lo que preguntan no se puede deshacer.
/// </summary>
public partial class MessageWindow : Window
{
    private enum Kind
    {
        Question,
        Warning,
        Error,
        Done,
    }

    // Iconos de Windows 11 (Segoe Fluent Icons) y su color del tema, que sigue el modo claro u oscuro.
    private const string WarningGlyph = "";
    private const string ErrorGlyph = "";
    private const string QuestionGlyph = "";
    private const string DoneGlyph = "";

    private MessageWindow(string title, string message, Kind kind, bool asksYesNo = false)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Body.Text = message;
        var (glyph, brush) = kind switch
        {
            Kind.Error => (ErrorGlyph, "SystemFillColorCriticalBrush"),
            Kind.Done => (DoneGlyph, "SystemFillColorSuccessBrush"),
            Kind.Question => (QuestionGlyph, "AccentTextFillColorPrimaryBrush"),
            _ => (WarningGlyph, "SystemFillColorCautionBrush"),
        };
        Symbol.Text = glyph;
        Symbol.SetResourceReference(ForegroundProperty, brush);
        if (!asksYesNo)
        {
            YesButton.Visibility = Visibility.Collapsed;
            NoButton.Content = "Aceptar";
        }
        Loaded += (_, _) => NoButton.Focus();
    }

    /// <summary>Pregunta Sí / No. True solo si el usuario pulsa «Sí».</summary>
    public static bool Confirm(string title, string message) => Show(new MessageWindow(title, message, Kind.Question, asksYesNo: true));

    /// <summary>Pregunta Sí / No antes de algo que borra o no se puede deshacer: lleva el icono de advertencia.</summary>
    public static bool ConfirmWarning(string title, string message) => Show(new MessageWindow(title, message, Kind.Warning, asksYesNo: true));

    /// <summary>Algo terminó bien y se pregunta qué hacer después (Sí / No).</summary>
    public static bool ConfirmAfterDone(string title, string message) => Show(new MessageWindow(title, message, Kind.Done, asksYesNo: true));

    /// <summary>Aviso con un solo botón «Aceptar».</summary>
    public static void Inform(string title, string message, bool isError = false) =>
        Show(new MessageWindow(title, message, isError ? Kind.Error : Kind.Warning));

    /// <summary>Algo terminó bien (p. ej. «desinstalado»), con un solo botón «Aceptar».</summary>
    public static void Done(string title, string message) => Show(new MessageWindow(title, message, Kind.Done));

    private static bool Show(MessageWindow window) => ModalWindow.Show(window) == true;

    private void OnYes(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnNo(object sender, RoutedEventArgs e) => DialogResult = false;
}

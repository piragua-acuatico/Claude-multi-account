using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ClaudeMultiAccount.App;

/// <summary>
/// Color de acento propio, el del logo (crema sobre negro), en lugar del de Windows: los botones principales,
/// casillas y selecciones se ven igual en cualquier equipo, también donde el acento de Windows es gris y un botón
/// principal parecería desactivado. No se usa el terracota de Claude para no imitar a Anthropic (como en el logo).
/// Sigue el modo claro u oscuro, como hace Windows con su acento.
/// </summary>
internal static class BrandAccent
{
    // Oscuro: relleno crema con texto negro. Claro: relleno casi negro con texto blanco. Contraste de sobra en los dos.
    private static readonly Color DarkFill = Color.FromRgb(0xEC, 0xE2, 0xCE);
    private static readonly Color DarkText = Color.FromRgb(0xE3, 0xD3, 0xB2);
    private static readonly Color LightFill = Color.FromRgb(0x23, 0x21, 0x1E);
    private static readonly Color LightText = Color.FromRgb(0x23, 0x21, 0x1E);

    // El tema Fluent usa nombres distintos según el control; todos reciben el mismo color.
    private static readonly string[] DefaultFillKeys =
    [
        "AccentFillColorDefaultBrush", "AccentDefaultBrush", "AccentButtonBackground",
        "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundStrokeChecked",
        "CheckBoxCheckBackgroundFillIndeterminate", "CheckBoxCheckBackgroundStrokeIndeterminate",
        "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedStroke", "ToggleSwitchFillOn",
    ];

    private static readonly string[] SecondaryFillKeys =
    [
        "AccentFillColorSecondaryBrush", "AccentSecondaryBrush", "AccentButtonBackgroundPointerOver",
        "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPointerOver",
        "CheckBoxCheckBackgroundFillIndeterminatePointerOver", "CheckBoxCheckBackgroundStrokeIndeterminatePointerOver",
        "RadioButtonOuterEllipseCheckedStrokePointerOver", "ToggleSwitchFillOnPointerOver",
    ];

    private static readonly string[] TertiaryFillKeys =
    [
        "AccentFillColorTertiaryBrush", "AccentTertiaryBrush", "AccentButtonBackgroundPressed",
        "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBackgroundStrokeCheckedPressed",
        "CheckBoxCheckBackgroundFillIndeterminatePressed", "CheckBoxCheckBackgroundStrokeIndeterminatePressed",
    ];

    private static readonly string[] TextKeys =
    [
        "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush",
        "SystemFillColorAttentionBrush",
    ];

    public static void Apply(Application app)
    {
        Set(app);
        // Si el usuario cambia entre modo claro y oscuro con la herramienta abierta.
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
                app.Dispatcher.InvokeAsync(() => Set(app));
        };
    }

    private static void Set(Application app)
    {
        var light = IsLightTheme();
        var fill = light ? LightFill : DarkFill;
        var text = light ? LightText : DarkText;
        foreach (var key in DefaultFillKeys) app.Resources[key] = Brush(fill, 1.0);
        foreach (var key in SecondaryFillKeys) app.Resources[key] = Brush(fill, 0.9);
        foreach (var key in TertiaryFillKeys) app.Resources[key] = Brush(fill, 0.8);
        foreach (var key in TextKeys) app.Resources[key] = Brush(text, 1.0);
    }

    private static SolidColorBrush Brush(Color color, double opacity)
    {
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static bool IsLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}

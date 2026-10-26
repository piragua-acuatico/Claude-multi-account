using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace ClaudeMultiAccount.App.Tray;

/// <summary>
/// Aspecto de Windows 11 para el menú de la bandeja (que es de Windows Forms y no recibe el tema Fluent de WPF):
/// colores del modo claro u oscuro de Windows, resaltado redondeado, sin la franja gris de iconos y con esquinas
/// redondeadas. Solo cambia cómo se dibuja; los elementos y lo que hacen son los mismos.
/// </summary>
internal sealed class FluentMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    // Una sola fuente para todas las veces que se abre el menú (cada Font nuevo es un recurso de Windows).
    private static readonly Font MenuFont = new("Segoe UI", 9.75f);

    private readonly Palette _palette;

    private FluentMenuRenderer(Palette palette) : base(new FlatColorTable(palette))
    {
        _palette = palette;
        RoundedEdges = false;
    }

    /// <summary>Prepara el menú con los colores del modo actual de Windows. Se llama cada vez que se abre.</summary>
    public static void Apply(Forms.ContextMenuStrip menu)
    {
        var palette = IsDarkMode() ? Palette.Dark : Palette.Light;
        menu.Renderer = new FluentMenuRenderer(palette);
        menu.BackColor = palette.Background;
        menu.ForeColor = palette.Text;
        menu.ShowImageMargin = false;
        menu.Padding = new Forms.Padding(4, 6, 4, 6);
        menu.Font = MenuFont;
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = palette.Text;
            if (item is Forms.ToolStripMenuItem)
            {
                item.Padding = new Forms.Padding(8, 6, 8, 6);
            }
        }
    }

    /// <summary>Esquinas redondeadas de Windows 11 (en Windows 10 no hace nada).</summary>
    public static void RoundCorners(Forms.ContextMenuStrip menu)
    {
        if (!menu.IsHandleCreated)
        {
            return;
        }
        var preference = DwmRound;
        _ = DwmSetWindowAttribute(menu.Handle, DwmWindowCornerPreference, ref preference, sizeof(int));
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }
        var bounds = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
        using var path = RoundedRectangle(bounds, 4);
        using var brush = new SolidBrush(_palette.Hover);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _palette.Text : _palette.Disabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(_palette.Border);
        e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_palette.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    private static GraphicsPath RoundedRectangle(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Modo de las apps de Windows (Configuración → Personalización → Colores). Si no se puede leer, claro.</summary>
    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    internal sealed record Palette(Color Background, Color Text, Color Disabled, Color Hover, Color Border)
    {
        public static readonly Palette Light = new(
            Color.FromArgb(0xF9, 0xF9, 0xF9), Color.FromArgb(0x1B, 0x1B, 0x1B), Color.FromArgb(0x8A, 0x8A, 0x8A),
            Color.FromArgb(0xEA, 0xEA, 0xEA), Color.FromArgb(0xE0, 0xE0, 0xE0));

        public static readonly Palette Dark = new(
            Color.FromArgb(0x2C, 0x2C, 0x2C), Color.FromArgb(0xFF, 0xFF, 0xFF), Color.FromArgb(0x8A, 0x8A, 0x8A),
            Color.FromArgb(0x3D, 0x3D, 0x3D), Color.FromArgb(0x45, 0x45, 0x45));
    }

    /// <summary>Colores planos (sin degradados) para lo que el renderizador base aún dibuja.</summary>
    private sealed class FlatColorTable(Palette palette) : Forms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Background;
        public override Color ImageMarginGradientBegin => palette.Background;
        public override Color ImageMarginGradientMiddle => palette.Background;
        public override Color ImageMarginGradientEnd => palette.Background;
        public override Color MenuBorder => palette.Border;
        public override Color MenuItemBorder => palette.Hover;
        public override Color MenuItemSelected => palette.Hover;
        public override Color MenuItemSelectedGradientBegin => palette.Hover;
        public override Color MenuItemSelectedGradientEnd => palette.Hover;
        public override Color MenuItemPressedGradientBegin => palette.Hover;
        public override Color MenuItemPressedGradientEnd => palette.Hover;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.Border;
    }

    private const int DwmWindowCornerPreference = 33;
    private const int DwmRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

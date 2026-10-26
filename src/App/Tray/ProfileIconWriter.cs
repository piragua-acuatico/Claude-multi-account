using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using ClaudeMultiAccount.Core.Shortcuts;

namespace ClaudeMultiAccount.App.Tray;

/// <summary>
/// Icono de un perfil para sus accesos directos: un círculo de su color con su inicial, como en la lista.
/// Formato .ico con imágenes PNG (admitido desde Windows Vista) en los tamaños que usa el Explorador.
/// </summary>
internal sealed class ProfileIconWriter : IProfileIconWriter
{
    private static readonly int[] Sizes = [16, 24, 32, 48, 256];

    public void Write(string icoPath, string colorHex, string glyph)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);
        var color = TryParse(colorHex) ?? ColorTranslator.FromHtml("#D97757");
        var frames = Sizes.Select(size => (size, png: Render(size, color, glyph))).ToList();

        // Se escribe aparte y se reemplaza de una vez: un acceso directo nunca ve un icono a medias.
        var temp = icoPath + ".tmp";
        using (var stream = File.Create(temp))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((short)0);            // reservado
            writer.Write((short)1);            // tipo: icono
            writer.Write((short)frames.Count);
            var offset = 6 + 16 * frames.Count;
            foreach (var (size, png) in frames)
            {
                writer.Write((byte)(size >= 256 ? 0 : size)); // 0 significa 256
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);         // sin paleta
                writer.Write((byte)0);
                writer.Write((short)1);        // planos
                writer.Write((short)32);       // bits por píxel
                writer.Write(png.Length);
                writer.Write(offset);
                offset += png.Length;
            }
            foreach (var (_, png) in frames)
            {
                writer.Write(png);
            }
        }
        File.Move(temp, icoPath, overwrite: true);
    }

    public void Delete(string icoPath)
    {
        if (File.Exists(icoPath))
        {
            File.Delete(icoPath);
        }
    }

    private static byte[] Render(int size, Color color, string glyph)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 0.5f, 0.5f, size - 1f, size - 1f);

            var text = string.IsNullOrWhiteSpace(glyph) ? "?" : glyph.Trim()[..1].ToUpperInvariant();
            using var font = new Font("Segoe UI", size * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var white = new SolidBrush(Color.White);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, white, new RectangleF(0, 0, size, size + size * 0.04f), format);
        }
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    private static Color? TryParse(string hex)
    {
        try
        {
            return ColorTranslator.FromHtml(hex);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }
}

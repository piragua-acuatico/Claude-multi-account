using System.Text;

namespace ClaudeMultiAccount.Core.Messaging;

/// <summary>
/// Enlace oficial de Claude Desktop para abrir un chat de Code nuevo con un texto escrito, sin enviarlo:
/// <c>claude://code/new?q=…&amp;folder=…</c> (comprobado en Claude 4.29: el texto queda en el cuadro de escribir y
/// el usuario decide si lo envía). Se pasa como argumento al Claude del perfil, nunca a Windows: Windows mandaría
/// cualquier enlace claude:// al Claude que tenga registrado, normalmente el principal.
/// </summary>
public static class CodeChatLink
{
    /// <summary>
    /// Texto que cabe con holgura: cada carácter con tilde ocupa 6 en el enlace y la línea de comandos de Windows
    /// admite 32 767 (Claude, además, corta el texto en 14 336).
    /// </summary>
    public const int MaxPromptLength = 4_000;

    /// <summary>El enlace y si el mensaje cupo entero (si no, el resto llega con el aviso automático del hook).</summary>
    public static (string Link, bool Complete) Build(MailboxMessage message, string? folder)
    {
        var (prompt, complete) = Prompt(message);
        var link = new StringBuilder("claude://code/new?q=").Append(Uri.EscapeDataString(prompt));
        if (!string.IsNullOrWhiteSpace(folder) && Path.IsPathFullyQualified(folder))
        {
            // Claude Desktop solo reconoce la carpeta con barras de Windows: con «C:/…» abre el chat sin carpeta
            // (comprobado el 2026-10-29). Claude Code suele escribir las rutas con barras normales.
            link.Append("&folder=").Append(Uri.EscapeDataString(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))));
        }
        return (link.ToString(), complete);
    }

    /// <summary>Lo que aparece escrito en el chat nuevo: quién lo manda, el mensaje y cómo contestar.</summary>
    public static (string Prompt, bool Complete) Prompt(MailboxMessage message)
    {
        var header = $"📨 Mensaje de «{message.FromName}», otra de mis cuentas de Claude (lo trae Claude Multi-Account):";
        var footer = $"Si te pido contestarle, usa la skill «mensajes-entre-cuentas» con --sin-chat para que le llegue al chat que ya tiene abierto.";
        var room = MaxPromptLength - header.Length - footer.Length - 8;
        var complete = message.Text.Length <= room;
        var body = complete
            ? message.Text
            : message.Text[..Math.Max(0, room - 160)].TrimEnd() +
              "…\n\n(El mensaje es más largo: el resto te llega con el aviso automático cuando envíe esto.)";
        return ($"{header}\n\n{body}\n\n{footer}", complete);
    }
}

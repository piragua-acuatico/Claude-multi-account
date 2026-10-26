using System.Text;

namespace ClaudeMultiAccount.Core.Launch;

/// <summary>
/// Construye la línea de comandos de CreateProcess a partir de una LISTA de argumentos, citando cada uno
/// según las reglas de CommandLineToArgvW. No pasa por ningún shell.
/// </summary>
public static class CommandLine
{
    public static string Build(string exePath, IEnumerable<string> arguments)
    {
        var sb = new StringBuilder();
        AppendQuoted(sb, exePath);
        foreach (var argument in arguments)
        {
            sb.Append(' ');
            AppendQuoted(sb, argument);
        }
        return sb.ToString();
    }

    public static string Quote(string argument)
    {
        var sb = new StringBuilder();
        AppendQuoted(sb, argument);
        return sb.ToString();
    }

    private static void AppendQuoted(StringBuilder sb, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            sb.Append(argument);
            return;
        }

        sb.Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                // Las barras antes de una comilla se duplican y la comilla se escapa.
                sb.Append('\\', backslashes * 2 + 1);
            }
            else
            {
                sb.Append('\\', backslashes);
            }
            backslashes = 0;
            sb.Append(c);
        }
        // Las barras finales se duplican para que no escapen la comilla de cierre.
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
    }
}

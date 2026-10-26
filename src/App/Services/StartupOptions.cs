namespace ClaudeMultiAccount.App.Services;

/// <summary>
/// Argumentos de ClaudeMA.exe. Los accesos directos de perfil usan <c>--launch &lt;id&gt;</c>.
/// Lo que no se reconoce se ignora: un acceso directo antiguo no debe impedir abrir la ventana.
/// </summary>
public sealed record StartupOptions(string? LaunchProfile, bool Uninstall = false)
{
    public const string LaunchSwitch = "--launch";

    /// <summary>Lo que lanza «Aplicaciones instaladas» de Windows para desinstalar.</summary>
    public const string UninstallSwitch = "--uninstall";

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? launch = null;
        var uninstall = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, UninstallSwitch, StringComparison.OrdinalIgnoreCase))
            {
                uninstall = true;
                continue;
            }
            if (string.Equals(arg, LaunchSwitch, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                launch = args[++i];
            }
            else if (arg.StartsWith(LaunchSwitch + "=", StringComparison.OrdinalIgnoreCase))
            {
                launch = arg[(LaunchSwitch.Length + 1)..];
            }
        }
        return new StartupOptions(string.IsNullOrWhiteSpace(launch) ? null : launch.Trim(), uninstall);
    }
}

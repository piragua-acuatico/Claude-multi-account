using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.App.ViewModels;

/// <summary>De dónde sale cada afirmación del panel, para no presentar suposiciones como hechos.</summary>
public enum Evidence
{
    /// <summary>Comprobado en las pruebas reales del proyecto (dos cuentas abiertas a la vez en el mismo equipo).</summary>
    Verified,

    /// <summary>Comprobado solo en parte.</summary>
    Partial,

    /// <summary>Según documentación oficial (Anthropic o Microsoft), sin probarlo aquí.</summary>
    Documented,

    /// <summary>Lo esperado por el diseño o lo que cuentan otros usuarios; sin comprobar.</summary>
    Unverified,

    /// <summary>Consecuencia directa de usar el mismo usuario de Windows; no necesita prueba.</summary>
    Inherent,
}

public sealed record SharingItem(string Title, string Detail, Evidence Evidence, string Source)
{
    public string EvidenceLabel => Evidence switch
    {
        Evidence.Verified => $"Comprobado en las {Source}",
        Evidence.Partial => $"Comprobado en parte en las {Source}",
        Evidence.Documented => $"Según la documentación ({Source})",
        Evidence.Inherent => "Así funciona Windows: todos los perfiles usan tu mismo usuario",
        _ => $"Sin comprobar ({Source})",
    };
}

public sealed record ProfileFoldersInfo(string Name, string DesktopDataDir, string ClaudeConfigDir);

/// <summary>
/// Panel "Qué comparte cada perfil". Explica qué separa la herramienta y qué sigue siendo común porque es de Windows o del proyecto.
/// </summary>
public sealed class SharingInfoViewModel
{
    public static readonly IReadOnlyList<SharingItem> SeparateItems =
    [
        new("Cuenta e inicio de sesión de Claude",
            "Cada perfil tiene su propia carpeta de datos de Claude Desktop, con su propia sesión. Abrir un perfil no toca la sesión de los demás.",
            Evidence.Verified, "pruebas del proyecto"),
        new("Chats normales (pestaña Chat)",
            "Están en los servidores de Anthropic, en la cuenta de cada perfil. Los ves en el perfil de esa cuenta y en claude.ai.",
            Evidence.Verified, "pruebas del proyecto"),
        new("Chats de Code",
            "La lista de chats y sus conversaciones se guardan en las carpetas de cada perfil, en tu PC. No pasan de un perfil a otro ni se ven en la web.",
            Evidence.Verified, "pruebas del proyecto"),
        new("Ajustes, memoria e historial de Claude Code",
            "Cada perfil usa su propia carpeta de configuración de Claude Code: su CLAUDE.md personal, sus permisos y su historial.",
            Evidence.Verified, "pruebas del proyecto"),
        new("Claude Code que usa la pestaña Code",
            "Cada perfil descarga y actualiza su propia copia.",
            Evidence.Verified, "pruebas del proyecto"),
        new("Conectores, skills y plugins de tu cuenta",
            "Los que se configuran en claude.ai van con cada cuenta.",
            Evidence.Documented, "Anthropic"),
        new("Ajustes locales de Claude Desktop (servidores MCP, preferencias)",
            "Deberían estar dentro de la carpeta de datos de cada perfil, así que cada uno tendría los suyos.",
            Evidence.Unverified, "diseño"),
        new("Tareas largas y modo agente",
            "Con dos perfiles abiertos no se vio ningún límite, pero no se ha probado a fondo con los dos trabajando a la vez.",
            Evidence.Partial, "pruebas del proyecto"),
    ];

    public static readonly IReadOnlyList<SharingItem> SharedItems =
    [
        new("Los archivos de tus proyectos",
            "Todos los perfiles ven y pueden cambiar las mismas carpetas. Evita que dos perfiles trabajen a la vez en la misma carpeta: se pueden pisar los cambios. Al abrir un perfil en una carpeta que ya usa otro, la herramienta avisa y puede crear una copia de trabajo de Git para él.",
            Evidence.Inherent, "Windows"),
        new("CLAUDE.md, la carpeta .claude y .mcp.json de cada proyecto",
            "Están dentro del proyecto, así que sus instrucciones y permisos valen para cualquier cuenta que lo abra. Revísalos antes de abrir con tu cuenta personal un proyecto del trabajo, o al revés.",
            Evidence.Documented, "Claude Code"),
        new("Git, GitHub (gh), SSH y tus variables de usuario",
            "Son de tu usuario de Windows: los commits y los push salen con la misma identidad desde cualquier perfil. Si una cuenta es de trabajo, configura el correo en ese repositorio (git config user.email).",
            Evidence.Inherent, "Windows"),
        new("El enlace claude:// de Windows",
            "Es uno solo para todo el equipo. Por eso el primer inicio de sesión de cada perfil se hace con los demás Claude cerrados, con el asistente.",
            Evidence.Unverified, "lo cuentan otros usuarios"),
        new("La protección de datos de Windows",
            "Cualquier programa que se ejecute con tu usuario puede leer las carpetas de todos los perfiles. La herramienta evita mezclas por accidente, pero no es una barrera de seguridad: para eso haría falta otro usuario de Windows.",
            Evidence.Documented, "Microsoft"),
    ];

    public SharingInfoViewModel(IEnumerable<Profile> profiles)
    {
        ProfileFolders = profiles.Select(p => p.Type == ProfileType.Main
                ? new ProfileFoldersInfo(p.Name, KnownPaths.MainDesktopDataDir, KnownPaths.MainClaudeConfigDir)
                : new ProfileFoldersInfo(p.Name, p.DesktopDataDir ?? "—", p.ClaudeConfigDir ?? "—"))
            .ToList();
    }

    public IReadOnlyList<SharingItem> Separate => SeparateItems;

    public IReadOnlyList<SharingItem> Shared => SharedItems;

    /// <summary>Dónde guarda sus datos cada perfil, para verlo en concreto.</summary>
    public IReadOnlyList<ProfileFoldersInfo> ProfileFolders { get; }
}

namespace ClaudeMultiAccount.Core.Paths;

/// <summary>
/// Rutas y nombres conocidos. Cada uno indica su origen: [P0] = comprobado en pruebas reales
/// con Claude Desktop, [OFICIAL] = documentación de Anthropic. No añadir rutas sin fuente.
/// </summary>
public static class KnownPaths
{
    /// <summary>[P0] Familia del paquete MSIX de Claude Desktop.</summary>
    public const string ClaudeMsixFamily = "Claude_pzs8sxrjxfjjc";

    /// <summary>[P0] Ejecutable relativo a la carpeta de instalación del paquete.</summary>
    public const string ClaudeExeRelative = @"app\Claude.exe";

    /// <summary>[P0] Datos de Desktop del perfil principal (ruta real, sin redirección MSIX).</summary>
    public const string MainDesktopDataDir = @"%APPDATA%\Claude";

    /// <summary>[OFICIAL F-01] Configuración por defecto de Claude Code.</summary>
    public const string MainClaudeConfigDir = @"%USERPROFILE%\.claude";

    /// <summary>Archivo de bloqueo de instancia única de Chromium dentro del directorio de datos. [P0] existe mientras la ventana está abierta.</summary>
    public const string LockFileName = "lockfile";
}

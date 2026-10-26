using ClaudeMultiAccount.Core.Paths;

namespace ClaudeMultiAccount.Core.Processes;

public enum LockState
{
    /// <summary>No hay archivo de bloqueo: el perfil no está abierto.</summary>
    Absent,

    /// <summary>Un proceso mantiene abierto el archivo: el perfil está abierto.</summary>
    Held,

    /// <summary>El archivo existe pero nadie lo usa (p. ej. tras un apagado brusco).</summary>
    Stale,

    Unknown,
}

public interface ILockProbe
{
    LockState Probe(string dataDir);
}

/// <summary>
/// Mira el archivo "lockfile" del directorio de datos. [P0] existe mientras la ventana está abierta.
/// [H] Chromium lo abre con escritura y solo permite compartir lectura: si al abrirlo para leer sin
/// permitir escritura hay violación de uso compartido, alguien lo tiene abierto. Solo lee; no escribe ni borra.
/// </summary>
public sealed class LockfileProbe : ILockProbe
{
    private const int SharingViolation = unchecked((int)0x80070020);

    public LockState Probe(string dataDir)
    {
        var path = Path.Combine(dataDir, KnownPaths.LockFileName);
        if (!File.Exists(path))
        {
            return LockState.Absent;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return LockState.Stale;
        }
        catch (FileNotFoundException)
        {
            return LockState.Absent;
        }
        catch (IOException ex) when (ex.HResult == SharingViolation)
        {
            return LockState.Held;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LockState.Unknown;
        }
    }
}

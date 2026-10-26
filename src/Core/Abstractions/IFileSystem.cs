namespace ClaudeMultiAccount.Core.Abstractions;

/// <summary>Operaciones de archivo que usa el núcleo. Interfaz para poder simularlas en pruebas.</summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);

    /// <summary>Escribe mediante un archivo temporal en la misma carpeta y lo reemplaza de una vez.</summary>
    void WriteAllTextAtomic(string path, string content);

    /// <summary>Subcarpetas directas (rutas completas); vacío si la carpeta no existe.</summary>
    IReadOnlyList<string> EnumerateDirectories(string path);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <summary>
    /// Lee dejando que otro proceso reemplace el archivo a la vez: la ventana consulta el estado cada 1,5 s mientras
    /// abrir o cerrar un perfil lo reescribe, y con el FileShare.Read de File.ReadAllText el reemplazo fallaría.
    /// </summary>
    public string ReadAllText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public IReadOnlyList<string> EnumerateDirectories(string path) =>
        Directory.Exists(path) ? Directory.GetDirectories(path) : [];

    public void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("La ruta no tiene carpeta contenedora.", nameof(path));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                // En disco antes de renombrar: tras un corte de luz no queda un archivo vacío con el nombre bueno.
                stream.Flush(flushToDisk: true);
            }
            MoveWithRetry(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>Reemplazo con unos pocos reintentos: un antivirus o un lector puede tener el destino abierto un instante.</summary>
    private static void MoveWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 5)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }
}

using System.Text.Json;
using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Paths;

namespace ClaudeMultiAccount.Core.Processes;

/// <summary>
/// Proceso raíz de un perfil lanzado por la herramienta. La hora de inicio (FILETIME UTC) evita confundirlo
/// con otro proceso que reciba el mismo PID más tarde.
/// </summary>
public sealed record RootProcess(int Pid, long StartTimeFileTimeUtc);

/// <summary>
/// state.json: qué proceso raíz lanzó la herramienta para cada perfil. Solo son pistas de ejecución
/// (sin secretos); si el archivo falta o está dañado se trata como vacío.
/// Sustituye al Job Object con nombre, que desaparece al terminar cma.exe.
/// </summary>
public sealed class RuntimeStateStore(AppPaths paths, IFileSystem fs)
{
    private sealed class StateDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<Guid, RootProcess> Roots { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public RootProcess? Get(Guid profileId) => Load().Roots.GetValueOrDefault(profileId);

    /// <summary>Las raíces de todos los perfiles que abrió la herramienta.</summary>
    public IReadOnlyCollection<RootProcess> All() => Load().Roots.Values;

    public void Set(Guid profileId, RootProcess root)
    {
        var document = Load();
        document.Roots[profileId] = root;
        Save(document);
    }

    public void Remove(Guid profileId)
    {
        var document = Load();
        if (document.Roots.Remove(profileId))
        {
            Save(document);
        }
    }

    private StateDocument Load()
    {
        if (!fs.FileExists(paths.StateFile))
        {
            return new StateDocument();
        }
        try
        {
            return JsonSerializer.Deserialize<StateDocument>(fs.ReadAllText(paths.StateFile), JsonOptions) ?? new StateDocument();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Solo son pistas: un archivo dañado o que no se puede leer en ese instante cuenta como vacío.
            return new StateDocument();
        }
    }

    private void Save(StateDocument document) =>
        fs.WriteAllTextAtomic(paths.StateFile, JsonSerializer.Serialize(document, JsonOptions));
}

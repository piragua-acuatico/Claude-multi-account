using System.Text.Json;

namespace ClaudeMultiAccount.Core.Install;

/// <summary>
/// Versiones de Claude Desktop con las que se ha probado la herramienta (docs/tested-versions.json).
/// Una versión que no está en la lista no se bloquea: solo se avisa (SECURITY.md T14).
/// </summary>
public sealed class TestedVersions
{
    private readonly HashSet<Version> _versions;

    private TestedVersions(IEnumerable<Version> versions) => _versions = [.. versions];

    public IReadOnlyCollection<Version> Versions => _versions;

    public bool IsTested(Version version) => _versions.Contains(version);

    /// <summary>Lee el documento. Lanza <see cref="FormatException"/> si no tiene la forma esperada.</summary>
    public static TestedVersions Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("claudeDesktop", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("La lista de versiones probadas no tiene la sección 'claudeDesktop'.");
            }

            var versions = new List<Version>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("version", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && Version.TryParse(value.GetString(), out var version)
                    && Passed(entry))
                {
                    versions.Add(version);
                }
            }
            return new TestedVersions(versions);
        }
        catch (JsonException ex)
        {
            throw new FormatException("La lista de versiones probadas no es JSON válido.", ex);
        }
    }

    /// <summary>Una versión anotada con un resultado que no empieza por «ok» (p. ej. «fallo…») no cuenta como probada.</summary>
    private static bool Passed(JsonElement entry) =>
        !entry.TryGetProperty("result", out var result)
        || result.ValueKind != JsonValueKind.String
        || (result.GetString() ?? "").StartsWith("ok", StringComparison.OrdinalIgnoreCase);
}

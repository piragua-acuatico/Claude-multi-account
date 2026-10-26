using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Backup;

public enum BackupItemKind
{
    /// <summary>profiles.json de la herramienta.</summary>
    ToolProfiles,

    /// <summary>settings.json de Claude Code del perfil.</summary>
    CodeSettings,

    /// <summary>CLAUDE.md personal (instrucciones y memoria) de Claude Code del perfil.</summary>
    CodeMemory,
}

public sealed record BackupEntry(
    BackupItemKind Kind,
    Guid? ProfileId,
    string ProfileName,
    string OriginalPath,
    string StoredFile,
    string Sha256);

public sealed class BackupManifest
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public string Note { get; set; } = "";
    public List<BackupEntry> Entries { get; set; } = [];

    /// <summary>Lo que no se copió y por qué (sin contenido).</summary>
    public List<string> Skipped { get; set; } = [];
}

public sealed record BackupInfo(string Folder, BackupManifest Manifest);

public enum RestoreState
{
    /// <summary>El archivo actual es igual al de la copia.</summary>
    Same,

    /// <summary>El archivo actual es distinto.</summary>
    Changed,

    /// <summary>Ahora no existe; restaurar lo crea.</summary>
    Missing,
}

public sealed record RestoreCandidate(BackupEntry Entry, RestoreState State);

public sealed record RestoreResult(int Restored, string? SafetyBackupFolder, IReadOnlyList<string> Errors);

/// <summary>
/// Busca cosas con pinta de credencial en un texto antes de copiarlo (SECURITY.md T9). Solo dice qué tipo de
/// cosa encontró, nunca el valor.
/// </summary>
public static partial class SecretScanner
{
    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]{10,}")]
    private static partial Regex AnthropicKey();

    [GeneratedRegex(@"(?i)(api[_-]?key|auth[_-]?token|access[_-]?token|refresh[_-]?token|client[_-]?secret|secret|password|bearer)""?\s*[:=]\s*""?[A-Za-z0-9_\-\./+=]{12,}")]
    private static partial Regex NamedSecret();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"\b(ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|xox[abprs]-[A-Za-z0-9\-]{10,}|AKIA[0-9A-Z]{16})\b")]
    private static partial Regex ServiceToken();

    public static IReadOnlyList<string> Find(string text)
    {
        var found = new List<string>();
        if (AnthropicKey().IsMatch(text))
        {
            found.Add("una clave de Anthropic");
        }
        if (NamedSecret().IsMatch(text))
        {
            found.Add("una clave, token o contraseña");
        }
        if (PrivateKey().IsMatch(text))
        {
            found.Add("una clave privada");
        }
        if (ServiceToken().IsMatch(text))
        {
            found.Add("un token de otro servicio");
        }
        return found;
    }
}

/// <summary>
/// Copias de seguridad de la configuración NO sensible. Lista cerrada:
/// profiles.json y, de cada perfil, el settings.json y el CLAUDE.md de Claude Code. Nada más: ni sesiones, ni
/// credenciales, ni historiales, ni cachés, ni los ajustes de Claude Desktop (pueden llevar tokens de MCP).
/// Un archivo con algo que parezca una clave no se copia. Restaurar enseña antes qué cambia y guarda una copia
/// de lo que reemplaza.
/// </summary>
public sealed class BackupService(AppPaths paths, IFileSystem fs, IEnvironment env, Func<DateTime> utcNow)
{
    public const string ManifestFile = "manifest.json";
    public const string SettingsFile = "settings.json";
    public const string MemoryFile = "CLAUDE.md";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public BackupInfo Create(ProfileDocument document, string note = "")
    {
        var candidates = new List<(BackupItemKind Kind, Profile? Profile, string Original)>
        {
            (BackupItemKind.ToolProfiles, null, paths.ProfilesFile),
        };
        foreach (var profile in document.Profiles)
        {
            var configDir = profile.Type == ProfileType.Main ? KnownPaths.MainClaudeConfigDir : profile.ClaudeConfigDir;
            if (string.IsNullOrWhiteSpace(configDir))
            {
                continue;
            }
            candidates.Add((BackupItemKind.CodeSettings, profile, Path.Combine(configDir, SettingsFile)));
            candidates.Add((BackupItemKind.CodeMemory, profile, Path.Combine(configDir, MemoryFile)));
        }
        return Write(candidates, note);
    }

    public IReadOnlyList<BackupInfo> List()
    {
        var result = new List<BackupInfo>();
        foreach (var folder in fs.EnumerateDirectories(paths.BackupsDir))
        {
            var manifestPath = Path.Combine(folder, ManifestFile);
            if (!fs.FileExists(manifestPath))
            {
                continue;
            }
            try
            {
                if (JsonSerializer.Deserialize<BackupManifest>(fs.ReadAllText(manifestPath), JsonOptions) is { } manifest)
                {
                    result.Add(new BackupInfo(folder, manifest));
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Una copia dañada no impide ver las demás.
            }
        }
        return result.OrderByDescending(b => b.Manifest.CreatedUtc).ToList();
    }

    public IReadOnlyList<RestoreCandidate> Preview(BackupInfo backup) =>
        backup.Manifest.Entries.Select(e =>
        {
            var current = PathUtil.Expand(e.OriginalPath, env);
            if (!fs.FileExists(current))
            {
                return new RestoreCandidate(e, RestoreState.Missing);
            }
            return new RestoreCandidate(e, Hash(fs.ReadAllText(current)) == e.Sha256 ? RestoreState.Same : RestoreState.Changed);
        }).ToList();

    /// <summary>Restaura lo elegido. Antes guarda en otra copia los archivos actuales que se van a reemplazar.</summary>
    public RestoreResult Restore(BackupInfo backup, IReadOnlyCollection<BackupEntry> selected)
    {
        var errors = new List<string>();
        var toRestore = new List<(BackupEntry Entry, string Text)>();
        foreach (var entry in selected)
        {
            var stored = Path.Combine(backup.Folder, entry.StoredFile.Replace('/', Path.DirectorySeparatorChar));
            if (!fs.FileExists(stored))
            {
                errors.Add($"{Describe(entry)}: falta en la copia.");
                continue;
            }
            var text = fs.ReadAllText(stored);
            if (Hash(text) != entry.Sha256)
            {
                errors.Add($"{Describe(entry)}: el archivo de la copia cambió desde que se hizo; no se restaura.");
                continue;
            }
            if (entry.Kind == BackupItemKind.ToolProfiles && ProfilesProblem(text) is { } problem)
            {
                errors.Add($"{Describe(entry)}: {problem}");
                continue;
            }
            if (toRestore.Any(r => string.Equals(r.Entry.OriginalPath, entry.OriginalPath, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"{Describe(entry)}: aparece dos veces en la copia; se restaura solo la primera.");
                continue;
            }
            toRestore.Add((entry, text));
        }

        string? safety = null;
        var existing = toRestore.Where(r => fs.FileExists(PathUtil.Expand(r.Entry.OriginalPath, env))).ToList();
        if (existing.Count > 0)
        {
            var info = Write(existing.Select(r => (r.Entry.Kind, (Profile?)null, r.Entry.OriginalPath)).ToList(),
                $"Copia automática antes de restaurar la del {backup.Manifest.CreatedUtc.ToLocalTime():dd/MM/yyyy HH:mm}",
                existing.ToDictionary(r => r.Entry.OriginalPath, r => (r.Entry.ProfileId, r.Entry.ProfileName), StringComparer.OrdinalIgnoreCase));
            safety = info.Folder;

            // Lo que no entró en la copia automática (p. ej. un settings.json con una clave) no se reemplaza:
            // si no, se perdería sin vuelta atrás.
            var saved = info.Manifest.Entries.Select(e => e.OriginalPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var unsaved in existing.Where(r => !saved.Contains(r.Entry.OriginalPath)))
            {
                errors.Add($"{Describe(unsaved.Entry)}: no se restaura porque el archivo actual no se pudo guardar antes " +
                           "(parece contener una clave); cópialo tú a mano si quieres reemplazarlo.");
                toRestore.Remove(unsaved);
            }
        }

        var restored = 0;
        foreach (var (entry, text) in toRestore)
        {
            try
            {
                fs.WriteAllTextAtomic(PathUtil.Expand(entry.OriginalPath, env), text);
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Describe(entry)}: no se pudo escribir ({ex.Message}).");
            }
        }
        return new RestoreResult(restored, safety, errors);
    }

    public static string Describe(BackupEntry entry) => entry.Kind switch
    {
        BackupItemKind.ToolProfiles => "Lista de perfiles (profiles.json)",
        BackupItemKind.CodeSettings => $"«{entry.ProfileName}»: ajustes de Claude Code (settings.json)",
        _ => $"«{entry.ProfileName}»: instrucciones personales de Claude Code (CLAUDE.md)",
    };

    private BackupInfo Write(IReadOnlyList<(BackupItemKind Kind, Profile? Profile, string Original)> candidates, string note,
        IReadOnlyDictionary<string, (Guid? Id, string Name)>? owners = null)
    {
        var now = utcNow();
        var folder = FreeFolder(now);
        var manifest = new BackupManifest { CreatedUtc = now, Note = note };
        var index = 0;
        foreach (var (kind, profile, original) in candidates)
        {
            var path = PathUtil.Expand(original, env);
            var owner = owners is not null && owners.TryGetValue(original, out var o) ? o : (profile?.Id, profile?.Name ?? "");
            var entryLabel = Describe(new BackupEntry(kind, owner.Item1, owner.Item2, original, "", ""));
            if (!PathUtil.IsUsableAbsolute(path) || !fs.FileExists(path))
            {
                continue;
            }

            var text = fs.ReadAllText(path);
            var secrets = SecretScanner.Find(text);
            if (secrets.Count > 0)
            {
                manifest.Skipped.Add($"{entryLabel}: no se copió porque parece contener {string.Join(" y ", secrets)}.");
                continue;
            }

            var stored = $"{++index:00}-{Path.GetFileName(path)}";
            fs.WriteAllTextAtomic(Path.Combine(folder, stored), text);
            manifest.Entries.Add(new BackupEntry(kind, owner.Item1, owner.Item2, original, stored, Hash(text)));
        }
        fs.WriteAllTextAtomic(Path.Combine(folder, ManifestFile), JsonSerializer.Serialize(manifest, JsonOptions));
        return new BackupInfo(folder, manifest);
    }

    private string FreeFolder(DateTime nowUtc)
    {
        var name = nowUtc.ToLocalTime().ToString("yyyy-MM-dd_HHmmss");
        var folder = Path.Combine(paths.BackupsDir, name);
        for (var n = 2; fs.DirectoryExists(folder); n++)
        {
            folder = Path.Combine(paths.BackupsDir, $"{name}-{n}");
        }
        return folder;
    }

    private string? ProfilesProblem(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ProfileDocument>(json, ProfileStore.JsonOptions);
            if (document is null)
            {
                return "la lista de perfiles de la copia está vacía.";
            }
            var errors = ProfileValidator.Validate(document, env);
            return errors.Count == 0 ? null : "la lista de perfiles de la copia no es válida: " + errors[0];
        }
        catch (JsonException)
        {
            return "la lista de perfiles de la copia no es JSON válido.";
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

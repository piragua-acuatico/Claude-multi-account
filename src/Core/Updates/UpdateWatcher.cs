using System.Text.Json;
using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Paths;

namespace ClaudeMultiAccount.Core.Updates;

/// <summary>
/// versions.json: la última versión de Claude Desktop que el usuario ya conoce (y si la comprobó en este equipo).
/// Solo números de versión y fechas; nada de Claude.
/// </summary>
public sealed class VersionWatchState
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Versión que el usuario ya vio (o comprobó). Null la primera vez.</summary>
    public string? KnownVersion { get; set; }

    /// <summary>Versión en la que pasó la comprobación de compatibilidad de este equipo.</summary>
    public string? CheckedVersion { get; set; }

    public DateTime? CheckedUtc { get; set; }
}

public sealed class VersionWatchStore(AppPaths paths, IFileSystem fs)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string FilePath => Path.Combine(paths.Home, "versions.json");

    public VersionWatchState Load()
    {
        if (!fs.FileExists(FilePath))
        {
            return new VersionWatchState();
        }
        try
        {
            return JsonSerializer.Deserialize<VersionWatchState>(fs.ReadAllText(FilePath), JsonOptions) ?? new VersionWatchState();
        }
        catch (JsonException)
        {
            return new VersionWatchState();
        }
    }

    public void Save(VersionWatchState state) => fs.WriteAllTextAtomic(FilePath, JsonSerializer.Serialize(state, JsonOptions));
}

/// <summary>Qué decir sobre la versión instalada de Claude Desktop.</summary>
public sealed record UpdateStatus(
    Version Installed,
    Version? Previous,
    bool IsTested,
    Version? NewestTested,
    bool CheckedOnThisPc,
    DateTime? CheckedUtc)
{
    /// <summary>Claude cambió de versión desde la última vez que el usuario lo supo y aún no se comprobó.</summary>
    public bool ChangedSinceKnown => Previous is not null && Previous != Installed && !CheckedOnThisPc;
}

/// <summary>
/// Compara la versión instalada con la conocida y con la lista de versiones probadas del
/// proyecto. La primera vez solo apunta la versión, sin avisar.
/// </summary>
public static class UpdateWatcher
{
    public static UpdateStatus Evaluate(ClaudeInstall install, VersionWatchState state, TestedVersions? tested)
    {
        var known = Version.TryParse(state.KnownVersion, out var k) ? k : null;
        var checkedOk = Version.TryParse(state.CheckedVersion, out var c) && c == install.Version;
        return new UpdateStatus(
            install.Version,
            known,
            tested?.IsTested(install.Version) ?? false,
            tested?.Versions.DefaultIfEmpty().Max(),
            checkedOk,
            checkedOk ? state.CheckedUtc : null);
    }

    /// <summary>True si hay que guardar: primera vez (se apunta sin avisar).</summary>
    public static bool RememberFirstSight(ClaudeInstall install, VersionWatchState state)
    {
        if (state.KnownVersion is not null)
        {
            return false;
        }
        state.KnownVersion = install.Version.ToString();
        return true;
    }

    /// <summary>Tras una comprobación de compatibilidad correcta: la versión queda conocida y comprobada.</summary>
    public static void MarkChecked(ClaudeInstall install, VersionWatchState state, DateTime nowUtc)
    {
        state.KnownVersion = install.Version.ToString();
        state.CheckedVersion = install.Version.ToString();
        state.CheckedUtc = nowUtc;
    }
}

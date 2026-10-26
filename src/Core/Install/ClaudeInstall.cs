namespace ClaudeMultiAccount.Core.Install;

public enum InstallKind
{
    Msix,
}

/// <summary>Instalación de Claude Desktop encontrada. Se resuelve en cada lanzamiento; nunca se guarda la ruta.</summary>
public sealed record ClaudeInstall(
    InstallKind Kind,
    Version Version,
    string PackageFullName,
    string PackageFamilyName,
    string InstallLocation,
    string ExePath);

public sealed record PackageInfo(string FullName, string InstallLocation)
{
    /// <summary>
    /// El nombre completo de un paquete es Nombre_Versión_Arquitectura_Recurso_Editor; el nombre no admite '_'.
    /// </summary>
    public Version? Version
    {
        get
        {
            var parts = FullName.Split('_');
            return parts.Length >= 2 && System.Version.TryParse(parts[1], out var v) ? v : null;
        }
    }
}

public sealed record LocateResult(ClaudeInstall? Install, string? Error)
{
    public bool Found => Install is not null;
}

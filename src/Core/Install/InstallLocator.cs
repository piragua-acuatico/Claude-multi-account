using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Paths;

namespace ClaudeMultiAccount.Core.Install;

/// <summary>
/// Localiza Claude Desktop. Solo MSIX: es lo comprobado. La instalación Win32
/// no existe en este equipo y sus rutas solo constan por la comunidad, así que no se busca.
/// </summary>
public sealed class InstallLocator(IPackageQuery packages, IFileSystem fs)
{
    public LocateResult Locate()
    {
        var candidates = packages.GetPackages(KnownPaths.ClaudeMsixFamily)
            .Where(p => p.Version is not null)
            .OrderByDescending(p => p.Version)
            .ToList();

        if (candidates.Count == 0)
        {
            return new LocateResult(null,
                "No se encontró Claude Desktop (paquete MSIX " + KnownPaths.ClaudeMsixFamily + ") para este usuario.");
        }

        foreach (var package in candidates)
        {
            var exe = Path.Combine(package.InstallLocation, KnownPaths.ClaudeExeRelative);
            if (fs.FileExists(exe))
            {
                return new LocateResult(new ClaudeInstall(
                    InstallKind.Msix,
                    package.Version!,
                    package.FullName,
                    KnownPaths.ClaudeMsixFamily,
                    package.InstallLocation,
                    exe), null);
            }
        }

        return new LocateResult(null,
            "El paquete de Claude está instalado, pero no se encontró " + KnownPaths.ClaudeExeRelative +
            " en su carpeta. Puede que Claude haya cambiado su estructura en una actualización.");
    }
}

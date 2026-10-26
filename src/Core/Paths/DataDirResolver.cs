using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Install;

namespace ClaudeMultiAccount.Core.Paths;

/// <summary>
/// Dónde quedan realmente los datos de un perfil de Desktop.
/// [P0] Con MSIX, una carpeta nueva bajo %APPDATA% que Claude crea termina en
/// %LOCALAPPDATA%\Packages\&lt;familia&gt;\LocalCache\Roaming\&lt;resto&gt;. Las carpetas que ya existían
/// en la ruta real (como %APPDATA%\Claude) se siguen usando ahí.
/// </summary>
public sealed class DataDirResolver(IEnvironment env, IFileSystem fs)
{
    /// <param name="expandedDataDir">Ruta tal como se pasa a --user-data-dir, ya expandida.</param>
    public string ResolveEffective(string expandedDataDir, ClaudeInstall? install)
    {
        if (fs.DirectoryExists(expandedDataDir))
        {
            return expandedDataDir;
        }

        var redirected = GetMsixRedirect(expandedDataDir, install);
        return redirected ?? expandedDataDir;
    }

    public string? GetMsixRedirect(string expandedDataDir, ClaudeInstall? install)
    {
        if (install is not { Kind: InstallKind.Msix })
        {
            return null;
        }

        var roaming = env.Get("APPDATA");
        var local = env.Get("LOCALAPPDATA");
        if (string.IsNullOrEmpty(roaming) || string.IsNullOrEmpty(local))
        {
            return null;
        }

        return PathUtil.TryGetRelative(roaming, expandedDataDir, out var relative)
            ? Path.Combine(local, "Packages", install.PackageFamilyName, "LocalCache", "Roaming", relative)
            : null;
    }
}

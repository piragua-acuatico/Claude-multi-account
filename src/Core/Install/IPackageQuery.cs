using ClaudeMultiAccount.Core.Native;

namespace ClaudeMultiAccount.Core.Install;

/// <summary>Consulta de solo lectura de los paquetes MSIX instalados para el usuario actual.</summary>
public interface IPackageQuery
{
    IReadOnlyList<PackageInfo> GetPackages(string packageFamilyName);
}

/// <summary>
/// Usa GetPackagesByPackageFamily y GetPackagePathByFullName (kernel32, solo lectura).
/// Evita lanzar PowerShell en cada lanzamiento.
/// </summary>
public sealed class AppxPackageQuery : IPackageQuery
{
    public IReadOnlyList<PackageInfo> GetPackages(string packageFamilyName)
    {
        uint count = 0;
        uint bufferLength = 0;
        var rc = NativeMethods.GetPackagesByPackageFamily(packageFamilyName, ref count, null, ref bufferLength, null);
        if (rc == NativeMethods.ERROR_SUCCESS || count == 0)
        {
            return [];
        }
        if (rc != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
        {
            return [];
        }

        var pointers = new IntPtr[count];
        var buffer = new char[bufferLength];
        rc = NativeMethods.GetPackagesByPackageFamily(packageFamilyName, ref count, pointers, ref bufferLength, buffer);
        if (rc != NativeMethods.ERROR_SUCCESS)
        {
            return [];
        }

        // El búfer contiene los nombres completos seguidos, cada uno terminado en '\0'.
        var fullNames = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<PackageInfo>();
        foreach (var fullName in fullNames)
        {
            var location = GetPackagePath(fullName);
            if (location is not null)
            {
                result.Add(new PackageInfo(fullName, location));
            }
        }
        return result;
    }

    private static string? GetPackagePath(string fullName)
    {
        uint length = 0;
        var rc = NativeMethods.GetPackagePathByFullName(fullName, ref length, null);
        if (rc != NativeMethods.ERROR_INSUFFICIENT_BUFFER || length == 0)
        {
            return null;
        }

        var path = new char[length];
        rc = NativeMethods.GetPackagePathByFullName(fullName, ref length, path);
        return rc == NativeMethods.ERROR_SUCCESS ? new string(path).TrimEnd('\0') : null;
    }
}

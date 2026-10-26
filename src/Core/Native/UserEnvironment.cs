using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeMultiAccount.Core.Native;

/// <summary>
/// CreateEnvironmentBlock (userenv.dll) con el token del usuario actual y sin heredar el entorno del proceso:
/// variables de sistema + de usuario + volátiles de la sesión, como las que pone el Explorador.
/// </summary>
internal static class UserEnvironment
{
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeKernelHandle tokenHandle);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeKernelHandle token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    public static IReadOnlyDictionary<string, string>? TryCreateDefault()
    {
        using var current = Process.GetCurrentProcess();
        if (!OpenProcessToken(current.Handle, TOKEN_QUERY | TOKEN_DUPLICATE, out var token))
        {
            return null;
        }

        using (token)
        {
            if (!CreateEnvironmentBlock(out var block, token, inherit: false))
            {
                return null;
            }

            try
            {
                return Parse(block);
            }
            finally
            {
                DestroyEnvironmentBlock(block);
            }
        }
    }

    /// <summary>El bloque son cadenas "NOMBRE=valor" terminadas en '\0', con un '\0' extra al final.</summary>
    private static Dictionary<string, string> Parse(IntPtr block)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        while (true)
        {
            var entry = Marshal.PtrToStringUni(block + offset);
            if (string.IsNullOrEmpty(entry))
            {
                break;
            }
            offset += (entry.Length + 1) * sizeof(char);

            // Las entradas que empiezan por '=' (p. ej. "=C:=C:\") son internas de la consola.
            var separator = entry.IndexOf('=', 1);
            if (entry[0] != '=' && separator > 0)
            {
                result[entry[..separator]] = entry[(separator + 1)..];
            }
        }
        return result;
    }
}

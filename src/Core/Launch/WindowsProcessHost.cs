using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ClaudeMultiAccount.Core.Native;
using ClaudeMultiAccount.Core.Processes;

namespace ClaudeMultiAccount.Core.Launch;

/// <summary>
/// Lanza el exe oficial y lo identifica por su proceso raíz (PID + hora de inicio) y su árbol de procesos.
/// No inyecta nada, no depura, no lee memoria ni líneas de comandos.
/// </summary>
public sealed class WindowsProcessHost : IProcessHost
{
    public RootProcess Start(LaunchPlan plan)
    {
        if (plan.TempDir is not null)
        {
            // La doc no garantiza que Claude Code la cree; está dentro de la configuración del perfil.
            try
            {
                Directory.CreateDirectory(plan.TempDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException("No se pudo crear la carpeta temporal del perfil.", ex);
            }
        }
        var info = Create(plan);
        try
        {
            // Sin la hora de inicio la raíz no se reconocería después y el perfil no se podría cerrar.
            if (!NativeMethods.GetProcessTimes(info.hProcess, out var creation, out _, out _, out _))
            {
                creation = TryGetStartTime(info.dwProcessId) ?? 0;
            }
            return new RootProcess(info.dwProcessId, creation);
        }
        finally
        {
            NativeMethods.CloseHandle(info.hThread);
            NativeMethods.CloseHandle(info.hProcess);
        }
    }

    private static NativeMethods.PROCESS_INFORMATION Create(LaunchPlan plan)
    {
        var block = Marshal.StringToHGlobalUni(BuildEnvironmentBlock(plan.Environment));
        try
        {
            const uint baseFlags = NativeMethods.CREATE_UNICODE_ENVIRONMENT;
            var workingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Si la herramienta corre dentro de un Job (p. ej. una terminal), se intenta salir de él para que
            // Claude no se cierre con ella. Si ese Job no lo permite, se lanza sin salir.
            if (TryCreate(plan, baseFlags | NativeMethods.CREATE_BREAKAWAY_FROM_JOB, block, workingDir, out var info, out var error)
                || (error == NativeMethods.ERROR_ACCESS_DENIED && TryCreate(plan, baseFlags, block, workingDir, out info, out error)))
            {
                return info;
            }
            throw new Win32Exception(error, "No se pudo iniciar Claude.");
        }
        finally
        {
            Marshal.FreeHGlobal(block);
        }
    }

    private static bool TryCreate(LaunchPlan plan, uint flags, IntPtr environmentBlock, string workingDir,
        out NativeMethods.PROCESS_INFORMATION info, out int error)
    {
        var startup = new NativeMethods.STARTUPINFOW { cb = Marshal.SizeOf<NativeMethods.STARTUPINFOW>() };
        var commandLine = new StringBuilder(CommandLine.Build(plan.ExePath, plan.Arguments));
        var ok = NativeMethods.CreateProcessW(plan.ExePath, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags,
            environmentBlock, workingDir, ref startup, out info);
        error = ok ? 0 : Marshal.GetLastWin32Error();
        return ok;
    }

    /// <summary>Bloque "NOMBRE=valor\0...\0\0", ordenado sin distinguir mayúsculas como pide CreateProcess.</summary>
    internal static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in environment.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (name.Length == 0 || name.Contains('=') || name.Contains('\0') || value.Contains('\0'))
            {
                continue;
            }
            sb.Append(name).Append('=').Append(value).Append('\0');
        }
        sb.Append('\0');
        return sb.ToString();
    }

    public IReadOnlyList<int>? GetProcessTree(RootProcess root)
    {
        if (TryGetStartTime(root.Pid) != root.StartTimeFileTimeUtc)
        {
            return null;
        }

        var children = SnapshotParents()
            .GroupBy(p => p.ParentPid)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Pid).ToList());

        var tree = new List<int> { root.Pid };
        var startTimes = new Dictionary<int, long> { [root.Pid] = root.StartTimeFileTimeUtc };
        for (var i = 0; i < tree.Count; i++)
        {
            var parent = tree[i];
            if (!children.TryGetValue(parent, out var candidates))
            {
                continue;
            }
            foreach (var child in candidates)
            {
                // Un hijo de verdad empezó después que su padre; si no, el PID del padre se reutilizó.
                var childStart = TryGetStartTime(child);
                if (childStart is not null && childStart >= startTimes[parent] && !startTimes.ContainsKey(child))
                {
                    startTimes[child] = childStart.Value;
                    tree.Add(child);
                }
            }
        }
        return tree;
    }

    /// <summary>
    /// Hora de inicio de un proceso vivo; null si no existe o ya terminó. Un proceso terminado sigue abriéndose
    /// mientras otro programa (p. ej. un antivirus) tenga una referencia a él: sin mirar el código de salida, la
    /// herramienta lo daba por abierto y no podía cerrarlo (T-02, 2026-10-17).
    /// </summary>
    internal static long? TryGetStartTime(int pid)
    {
        using var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle.IsInvalid)
        {
            return null;
        }
        if (!NativeMethods.GetExitCodeProcess(handle, out var exitCode) || exitCode != NativeMethods.STILL_ACTIVE)
        {
            return null;
        }
        return NativeMethods.GetProcessTimes(handle, out var creation, out _, out _, out _) ? creation : null;
    }

    private static List<(int Pid, int ParentPid, string Name)> SnapshotParents()
    {
        var result = new List<(int, int, string)>();
        using var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            return result;
        }

        var entry = new NativeMethods.PROCESSENTRY32W { dwSize = Marshal.SizeOf<NativeMethods.PROCESSENTRY32W>() };
        if (!NativeMethods.Process32FirstW(snapshot, ref entry))
        {
            return result;
        }
        do
        {
            result.Add((entry.th32ProcessID, entry.th32ParentProcessID, entry.szExeFile));
        }
        while (NativeMethods.Process32NextW(snapshot, ref entry));
        return result;
    }

    /// <summary>
    /// Procesos principales de Claude Desktop (los Claude.exe de esa ruta cuyo padre no es otro Claude.exe): uno por
    /// cada perfil abierto, lo haya abierto la herramienta o no.
    /// </summary>
    public IReadOnlyList<int> FindRootProcesses(string exePath)
    {
        var name = Path.GetFileName(exePath);
        var claude = SnapshotParents().Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        var claudePids = claude.Select(p => p.Pid).ToHashSet();
        return claude
            .Where(p => !claudePids.Contains(p.ParentPid) && IsExe(p.Pid, exePath))
            .Select(p => p.Pid)
            .ToList();
    }

    /// <summary>Si el proceso es ese ejecutable (Claude Code también se llama claude.exe, pero está en otra carpeta).</summary>
    private static bool IsExe(int pid, string exePath)
    {
        using var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle.IsInvalid)
        {
            return false;
        }
        var buffer = new StringBuilder(1024);
        var size = buffer.Capacity;
        return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size)
               && string.Equals(buffer.ToString(), exePath, StringComparison.OrdinalIgnoreCase);
    }

    public void RequestClose(IReadOnlyList<int> processIds)
    {
        foreach (var id in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    process.CloseMainWindow();
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // El proceso ya terminó.
            }
        }
    }

    public void Terminate(IReadOnlyList<int> processIds)
    {
        foreach (var id in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                process.Kill();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Ya terminó o no se pudo; se informa por el estado final.
            }
        }
    }

    /// <summary>
    /// Process.MainWindowHandle no sirve aquí: con Claude escondido en la bandeja vale 0 y no había nada que traer
    /// al frente. Se buscan las ventanas de primer nivel de esos procesos, visibles o no, y se toma la ventana
    /// principal de Chromium (clase Chrome_WidgetWin_1, con título y sin dueño).
    /// </summary>
    public bool TryFocus(IReadOnlyList<int> processIds)
    {
        if (processIds.Count == 0)
        {
            return false;
        }
        // Claude tiene además ventanas auxiliares de la misma clase (una sin barra de título, otra diminuta y oculta):
        // la principal es la de mayor tamaño con barra de título. Mostrar otra abriría una ventana rara.
        var pids = processIds.ToHashSet();
        var window = IntPtr.Zero;
        long bestArea = 0;
        NativeMethods.EnumWindows((handle, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(handle, out var pid);
            if (pids.Contains((int)pid) && IsMainChromiumWindow(handle) && NativeMethods.GetWindowRect(handle, out var rect))
            {
                var area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                if (area > bestArea)
                {
                    bestArea = area;
                    window = handle;
                }
            }
            return true;
        }, IntPtr.Zero);

        if (window == IntPtr.Zero)
        {
            return false;
        }
        NativeMethods.ShowWindow(window, NativeMethods.IsIconic(window) ? NativeMethods.SW_RESTORE : NativeMethods.SW_SHOW);
        // Windows solo deja ponerla delante si la herramienta es la ventana activa (lo normal al pulsar su botón);
        // si no, al menos queda visible y parpadea en la barra de tareas. Cuenta como conseguido en los dos casos.
        _ = NativeMethods.SetForegroundWindow(window);
        return true;
    }

    public void Activate(LaunchPlan plan)
    {
        // Claude tiene que poder ponerse delante al recibir el encargo, aunque esta herramienta sea la de delante.
        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        var info = Create(plan);
        NativeMethods.CloseHandle(info.hThread);
        NativeMethods.CloseHandle(info.hProcess);
    }

    private static bool IsMainChromiumWindow(IntPtr handle)
    {
        if (NativeMethods.GetWindow(handle, NativeMethods.GW_OWNER) != IntPtr.Zero || NativeMethods.GetWindowTextLength(handle) == 0
            || (NativeMethods.GetWindowLong(handle, NativeMethods.GWL_STYLE) & NativeMethods.WS_CAPTION) != NativeMethods.WS_CAPTION)
        {
            return false;
        }
        var className = new StringBuilder(64);
        return NativeMethods.GetClassName(handle, className, className.Capacity) > 0
               && className.ToString() == "Chrome_WidgetWin_1";
    }
}

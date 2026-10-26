using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ClaudeMultiAccount.Core.Shortcuts;

namespace ClaudeMultiAccount.Core.Native;

/// <summary>
/// Accesos directos con la interfaz IShellLink de Windows (la misma que usa el Explorador). No ejecuta scripts
/// (nada de WScript ni PowerShell) y no escribe en el registro.
/// </summary>
public sealed class ShellLinkFile : IShortcutFile
{
    private const int MaxPath = 1024;
    private const int ShowNormal = 1;

    public bool Exists(string path) => File.Exists(path);

    public ShortcutTarget? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var exe = new StringBuilder(MaxPath);
            link.GetPath(exe, exe.Capacity, IntPtr.Zero, 0);
            var args = new StringBuilder(MaxPath);
            link.GetArguments(args, args.Capacity);
            var dir = new StringBuilder(MaxPath);
            link.GetWorkingDirectory(dir, dir.Capacity);
            var icon = new StringBuilder(MaxPath);
            link.GetIconLocation(icon, icon.Capacity, out _);
            var description = new StringBuilder(MaxPath);
            link.GetDescription(description, description.Capacity);
            return new ShortcutTarget(exe.ToString(), args.ToString(), dir.ToString(), icon.ToString(), description.ToString());
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public void Write(string path, ShortcutTarget target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(target.ExePath);
            link.SetArguments(target.Arguments);
            link.SetWorkingDirectory(target.WorkingDirectory);
            link.SetIconLocation(target.IconPath, 0);
            link.SetDescription(target.Description);
            link.SetShowCmd(ShowNormal);
            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass;

    /// <summary>Orden de métodos de la vtable de IShellLinkW (shobjidl_core.h); no se puede reordenar.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}

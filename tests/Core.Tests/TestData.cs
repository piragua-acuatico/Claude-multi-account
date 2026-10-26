using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Tests;

internal static class TestData
{
    public const string InstallLocation = @"C:\Program Files\WindowsApps\Claude_2.37337.1.0_x64__pzs8sxrjxfjjc";

    public static ClaudeInstall Install() => new(
        InstallKind.Msix,
        new Version(2, 37337, 1, 0),
        "Claude_2.37337.1.0_x64__pzs8sxrjxfjjc",
        "Claude_pzs8sxrjxfjjc",
        InstallLocation,
        InstallLocation + @"\app\Claude.exe");

    public static Profile MainProfile(string name = "Claude Max") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = ProfileType.Main,
    };

    public static Profile Isolated(string name = "Claude Pro", string slug = "prueba") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = ProfileType.DesktopIsolated,
        DesktopDataDir = $@"%APPDATA%\CMA-{slug}",
        ClaudeConfigDir = $@"%USERPROFILE%\.claude-{slug}",
    };

    public static ProfileDocument Document(params Profile[] profiles)
    {
        var document = new ProfileDocument();
        document.Profiles.AddRange(profiles);
        return document;
    }
}

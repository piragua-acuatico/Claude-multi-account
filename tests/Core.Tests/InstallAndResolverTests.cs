using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Paths;

namespace ClaudeMultiAccount.Core.Tests;

public class InstallLocatorTests
{
    private const string Old = @"C:\Program Files\WindowsApps\Claude_2.30000.0.0_x64__pzs8sxrjxfjjc";
    private const string New = @"C:\Program Files\WindowsApps\Claude_2.37337.1.0_x64__pzs8sxrjxfjjc";

    [Fact]
    public void PicksHighestVersionWithExistingExe()
    {
        var fs = new FakeFileSystem();
        fs.Files[Old + @"\app\Claude.exe"] = "";
        fs.Files[New + @"\app\Claude.exe"] = "";
        var query = new FakePackageQuery(
            new PackageInfo("Claude_2.30000.0.0_x64__pzs8sxrjxfjjc", Old),
            new PackageInfo("Claude_2.37337.1.0_x64__pzs8sxrjxfjjc", New));

        var result = new InstallLocator(query, fs).Locate();

        Assert.True(result.Found);
        Assert.Equal(new Version(2, 37337, 1, 0), result.Install!.Version);
        Assert.Equal(New + @"\app\Claude.exe", result.Install.ExePath);
    }

    [Fact]
    public void FallsBackWhenNewestHasNoExe()
    {
        var fs = new FakeFileSystem();
        fs.Files[Old + @"\app\Claude.exe"] = "";
        var query = new FakePackageQuery(
            new PackageInfo("Claude_2.30000.0.0_x64__pzs8sxrjxfjjc", Old),
            new PackageInfo("Claude_2.37337.1.0_x64__pzs8sxrjxfjjc", New));

        Assert.Equal(new Version(2, 30000, 0, 0), new InstallLocator(query, fs).Locate().Install!.Version);
    }

    [Fact]
    public void NotInstalled_ReturnsError()
    {
        var result = new InstallLocator(new FakePackageQuery(), new FakeFileSystem()).Locate();
        Assert.False(result.Found);
        Assert.Contains("No se encontró", result.Error);
    }

    [Fact]
    public void ExeMissing_ReturnsStructureError()
    {
        var query = new FakePackageQuery(new PackageInfo("Claude_2.37337.1.0_x64__pzs8sxrjxfjjc", New));
        var result = new InstallLocator(query, new FakeFileSystem()).Locate();
        Assert.False(result.Found);
        Assert.Contains("estructura", result.Error);
    }

    [Fact]
    public void PackageVersion_ParsesFromFullName() =>
        Assert.Equal(new Version(2, 37337, 1, 0), new PackageInfo("Claude_2.37337.1.0_x64__pzs8sxrjxfjjc", New).Version);
}

public class DataDirResolverTests
{
    private readonly FakeEnvironment _env = new();
    private readonly FakeFileSystem _fs = new();

    [Fact]
    public void ExistingRealPath_IsUsed()
    {
        var real = FakeEnvironment.AppData + @"\Claude";
        _fs.Directories.Add(real);
        Assert.Equal(real, new DataDirResolver(_env, _fs).ResolveEffective(real, TestData.Install()));
    }

    [Fact]
    public void NewPathUnderRoaming_IsRedirectedToLocalCache_ForMsix()
    {
        var requested = FakeEnvironment.AppData + @"\CMA-prueba";
        var effective = new DataDirResolver(_env, _fs).ResolveEffective(requested, TestData.Install());
        Assert.Equal(FakeEnvironment.LocalAppData + @"\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\CMA-prueba", effective);
    }

    [Fact]
    public void PathOutsideRoaming_IsNotRedirected()
    {
        var requested = @"D:\perfiles\pro";
        Assert.Equal(requested, new DataDirResolver(_env, _fs).ResolveEffective(requested, TestData.Install()));
    }

    [Fact]
    public void WithoutInstall_NoRedirect()
    {
        var requested = FakeEnvironment.AppData + @"\CMA-prueba";
        Assert.Equal(requested, new DataDirResolver(_env, _fs).ResolveEffective(requested, null));
    }
}

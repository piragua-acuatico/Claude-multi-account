using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.App.ViewModels;

namespace ClaudeMultiAccount.App.Tests;

public class SharingInfoTests
{
    [Fact]
    public void EveryItemSaysWhereItsClaimComesFrom()
    {
        foreach (var item in SharingInfoViewModel.SeparateItems.Concat(SharingInfoViewModel.SharedItems))
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Detail));
            Assert.False(string.IsNullOrWhiteSpace(item.Source));
            Assert.False(string.IsNullOrWhiteSpace(item.EvidenceLabel));
        }
    }

    [Fact]
    public void Labels_DistinguishVerifiedFromUnverified()
    {
        var verified = new SharingItem("a", "b", Evidence.Verified, "pruebas del proyecto");
        var unverified = new SharingItem("a", "b", Evidence.Unverified, "diseño");

        Assert.Equal("Comprobado en las pruebas del proyecto", verified.EvidenceLabel);
        Assert.Equal("Sin comprobar (diseño)", unverified.EvidenceLabel);
    }

    [Fact]
    public void KeySharedResources_AreListed()
    {
        var shared = string.Join(" ", SharingInfoViewModel.SharedItems.Select(i => i.Title));
        Assert.Contains("Git", shared);
        Assert.Contains("SSH", shared);
        Assert.Contains("CLAUDE.md", shared);
        Assert.Contains("claude://", shared);
    }

    [Fact]
    public void ProfileFolders_ShowEachProfilesOwnFolders()
    {
        var main = TestProfiles.MainProfile();
        var pro = TestProfiles.Pro();

        var info = new SharingInfoViewModel([main, pro]);

        Assert.Equal(@"%APPDATA%\Claude", info.ProfileFolders[0].DesktopDataDir);
        Assert.Equal(@"%USERPROFILE%\.claude", info.ProfileFolders[0].ClaudeConfigDir);
        Assert.Equal(pro.DesktopDataDir, info.ProfileFolders[1].DesktopDataDir);
        Assert.Equal(pro.ClaudeConfigDir, info.ProfileFolders[1].ClaudeConfigDir);
    }

    [Fact]
    public async Task MainWindowButton_OpensThePanelWithTheCurrentProfiles()
    {
        var service = new FakeProfileService { Document = TestProfiles.Document(TestProfiles.MainProfile(), TestProfiles.Pro()) };
        var dialogs = new FakeDialogService(answer: true);
        var vm = new MainViewModel(service, dialogs);
        await vm.InitializeAsync(new StartupOptions(LaunchProfile: null));

        vm.ShowSharingInfoCommand.Execute(null);

        Assert.Equal(new[] { "Claude Max", "Claude Pro" }, Assert.Single(dialogs.SharingShown).ProfileFolders.Select(p => p.Name));
    }
}

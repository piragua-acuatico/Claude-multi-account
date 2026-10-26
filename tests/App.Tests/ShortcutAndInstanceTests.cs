using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.App.ViewModels;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Shortcuts;

namespace ClaudeMultiAccount.App.Tests;

public class InstanceMessageTests
{
    [Theory]
    [InlineData(null, "show")]
    [InlineData("9cff3686-46fb-4ee4-98a3-04aa62d21dec", "launch 9cff3686-46fb-4ee4-98a3-04aa62d21dec")]
    [InlineData("Claude Pro", "launch Claude Pro")]
    public void SerializeAndParse_RoundTrip(string? profile, string wire)
    {
        var message = new InstanceMessage(profile);
        Assert.Equal(wire, message.Serialize());
        Assert.Equal(message, InstanceMessage.Parse(wire));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("launch ")]
    [InlineData("borrar todo")]
    [InlineData("launch a\u0007b")]
    public void Parse_IgnoresAnythingElse(string? wire)
    {
        Assert.Null(InstanceMessage.Parse(wire));
    }

    [Fact]
    public void Parse_RejectsVeryLongLines()
    {
        Assert.Null(InstanceMessage.Parse("launch " + new string('x', 1000)));
    }
}

public class SingleInstanceTests
{
    private static string UniqueName() => "ClaudeMultiAccount-Prueba-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void SecondAcquire_FailsWhileTheFirstIsAlive()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(name));
    }

    [Fact]
    public async Task Send_ReachesTheListeningInstance()
    {
        var name = UniqueName();
        using var primary = SingleInstance.TryAcquire(name)!;
        var received = new TaskCompletionSource<InstanceMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.Listen(m => received.TrySetResult(m));

        Assert.True(SingleInstance.TrySend(name, new InstanceMessage("Claude Pro")));

        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Claude Pro", message.LaunchProfile);
    }

    [Fact]
    public void Send_WithoutAListener_FailsInsteadOfHanging()
    {
        Assert.False(SingleInstance.TrySend(UniqueName(), new InstanceMessage(null)));
    }
}

public class ShortcutFlowTests
{
    private static readonly StartupOptions NoArgs = new(LaunchProfile: null);
    private readonly FakeProfileService _service = new();
    private readonly FakeDialogService _dialogs = new(answer: true);
    private readonly MainViewModel _vm;
    private readonly Profile _main = TestProfiles.MainProfile();
    private readonly Profile _pro = TestProfiles.Pro();

    public ShortcutFlowTests()
    {
        _service.Document = TestProfiles.Document(_main, _pro);
        _vm = new MainViewModel(_service, _dialogs);
    }

    private ProfileItemViewModel Item(Guid id) => _vm.Profiles.Single(p => p.Id == id);

    [Fact]
    public async Task CreateShortcut_OnTheDesktop_ReportsWhere()
    {
        await _vm.InitializeAsync(NoArgs);
        _dialogs.ShortcutChoice = ShortcutLocation.Desktop;

        await _vm.CreateShortcutAsync(Item(_pro.Id));

        Assert.Equal((_pro.Id, ShortcutLocation.Desktop), Assert.Single(_service.ShortcutsCreated));
        Assert.Contains(@"Desktop\Claude Pro.lnk", _vm.StatusMessage);
    }

    [Fact]
    public async Task CreateShortcut_Cancelled_DoesNothing()
    {
        await _vm.InitializeAsync(NoArgs);

        await _vm.CreateShortcutAsync(Item(_pro.Id));

        Assert.Empty(_service.ShortcutsCreated);
    }

    [Fact]
    public async Task CreateShortcut_AlsoForTheMainProfile()
    {
        await _vm.InitializeAsync(NoArgs);
        _dialogs.ShortcutChoice = ShortcutLocation.StartMenu;

        await _vm.CreateShortcutAsync(Item(_main.Id));

        Assert.Equal((_main.Id, ShortcutLocation.StartMenu), Assert.Single(_service.ShortcutsCreated));
    }

    [Fact]
    public async Task CreateShortcut_Failure_IsReported()
    {
        await _vm.InitializeAsync(NoArgs);
        _dialogs.ShortcutChoice = ShortcutLocation.Desktop;
        _service.ShortcutError = "Windows no pudo crear el acceso directo.";

        await _vm.CreateShortcutAsync(Item(_pro.Id));

        Assert.Equal("No se creó el acceso directo: Windows no pudo crear el acceso directo.", _vm.StatusMessage);
    }

    [Fact]
    public async Task RemoveConfirmation_MentionsItsShortcuts()
    {
        _pro.Shortcuts = [@"C:\Users\prueba\Desktop\Claude Pro.lnk"];
        await _vm.InitializeAsync(NoArgs);

        await _vm.RemoveAsync(Item(_pro.Id));

        Assert.Contains("se borrarán sus accesos directos (1)", Assert.Single(_dialogs.Asked).Message);
    }

    [Fact]
    public async Task LaunchRequest_FromAShortcut_OpensThatProfile()
    {
        await _vm.InitializeAsync(NoArgs);

        await _vm.LaunchRequestedAsync(_pro.Id.ToString("D"));

        Assert.Equal(new[] { _pro.Id }, _service.Launched);
    }

    [Fact]
    public async Task LaunchRequest_ForARemovedProfile_SaysItNoLongerExists()
    {
        await _vm.InitializeAsync(NoArgs);

        await _vm.LaunchRequestedAsync(Guid.NewGuid().ToString("D"));

        Assert.Empty(_service.Launched);
        Assert.StartsWith("No existe el perfil", _vm.StatusMessage);
    }
}

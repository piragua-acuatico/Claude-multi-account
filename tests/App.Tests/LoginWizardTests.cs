using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.App.ViewModels;
using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.App.Tests;

public class LoginWizardTests
{
    private static readonly StartupOptions NoArgs = new(LaunchProfile: null);
    private readonly FakeProfileService _service = new();
    private readonly FakeDialogService _dialogs = new(answer: true);
    private readonly MainViewModel _vm;
    private readonly Profile _main = TestProfiles.MainProfile();
    private readonly Profile _pro = TestProfiles.Pro();
    private readonly Profile _work = TestProfiles.NewIsolated();

    public LoginWizardTests()
    {
        _main.EmailHint = "max@correo.com";
        _service.Document = TestProfiles.Document(_main, _pro, _work);
        _vm = new MainViewModel(_service, _dialogs);
    }

    private ProfileItemViewModel Item(Guid id) => _vm.Profiles.Single(p => p.Id == id);

    /// <summary>Abre "Claude Trabajo" y deja el asistente abierto (sin terminar) para inspeccionarlo.</summary>
    private async Task<LoginWizardViewModel> OpenWizardAsync()
    {
        await _vm.InitializeAsync(NoArgs);
        await _vm.OpenAsync(Item(_work.Id));
        return Assert.Single(_dialogs.Wizards);
    }

    [Fact]
    public async Task FirstOpenOfANewProfile_ShowsTheWizardInsteadOfLaunching()
    {
        var wizard = await OpenWizardAsync();

        Assert.Equal(LoginWizardStep.CloseOthers, wizard.Step);
        Assert.Empty(_service.Launched);
        Assert.Contains("volverá a salir", _vm.StatusMessage);
    }

    [Fact]
    public async Task ProfilesAlreadyUsed_OpenWithoutTheWizard()
    {
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(_pro.Id));

        Assert.Empty(_dialogs.Wizards);
        Assert.Equal(new[] { _pro.Id }, _service.Launched);
    }

    [Fact]
    public async Task Step1_BlocksWhileOtherClaudesAreOpen()
    {
        _service.Statuses[_main.Id] = FakeProfileService.RunningOutside();
        _service.Statuses[_pro.Id] = FakeProfileService.RunningFromTool(10);
        var wizard = await OpenWizardAsync();

        Assert.False(wizard.CanGoNext);
        Assert.Equal(2, wizard.OpenOthers.Count);
        Assert.Contains(wizard.OpenOthers, l => l.Contains("Claude Max") && l.Contains("Salir"));
        Assert.Contains(wizard.OpenOthers, l => l.Contains("Claude Pro") && l.Contains("botón"));

        // El usuario sale de Max por la bandeja y la herramienta cierra Pro (con su confirmación).
        _service.Statuses[_main.Id] = FakeProfileService.Closed();
        _service.OnClose = (p, _) =>
        {
            _service.Statuses[p.Id] = FakeProfileService.Closed();
            return new CloseOutcome(CloseOutcomeKind.Terminated, "cerrado");
        };
        await wizard.CloseOthersCommand.ExecuteAsync();

        Assert.Equal((_pro.Id, true), Assert.Single(_service.CloseCalls));
        Assert.True(wizard.AllOthersClosed);
        Assert.True(wizard.CanGoNext);
    }

    [Fact]
    public async Task FullFlow_LaunchesOnlyThisProfile_AndSavesTheConfirmation()
    {
        var wizard = await OpenWizardAsync();
        var completed = false;
        wizard.Completed += (_, _) => completed = true;

        await wizard.NextCommand.ExecuteAsync();
        Assert.Equal(LoginWizardStep.SignIn, wizard.Step);
        Assert.False(wizard.CanGoNext);

        await wizard.OpenProfileCommand.ExecuteAsync();
        Assert.Equal(new[] { _work.Id }, _service.Launched);
        Assert.Empty(_service.Recorded);
        Assert.True(wizard.CanGoNext);

        await wizard.NextCommand.ExecuteAsync();
        Assert.Equal(LoginWizardStep.Verify, wizard.Step);
        Assert.Equal("Terminar", wizard.NextLabel);
        Assert.False(wizard.CanGoNext);

        wizard.Email = " trabajo@empresa.com ";
        wizard.AccountIsCorrect = true;
        await wizard.NextCommand.ExecuteAsync();

        Assert.True(completed);
        Assert.Equal((_work.Id, "trabajo@empresa.com"), Assert.Single(_service.LoginsConfirmed));
    }

    [Fact]
    public async Task EmailOfAnotherProfile_BlocksFinishing()
    {
        var wizard = await OpenWizardAsync();
        await wizard.NextCommand.ExecuteAsync();
        await wizard.OpenProfileCommand.ExecuteAsync();
        await wizard.NextCommand.ExecuteAsync();

        wizard.Email = "MAX@correo.com";
        wizard.AccountIsCorrect = true;

        Assert.Contains("Claude Max", wizard.DuplicateWarning);
        Assert.False(wizard.CanGoNext);
    }

    [Fact]
    public async Task WrongAccount_GoesBackToSignIn()
    {
        var wizard = await OpenWizardAsync();
        await wizard.NextCommand.ExecuteAsync();
        await wizard.OpenProfileCommand.ExecuteAsync();
        await wizard.NextCommand.ExecuteAsync();

        wizard.AccountIsWrong = true;
        Assert.False(wizard.CanGoNext);
        wizard.BackCommand.Execute(null);

        Assert.Equal(LoginWizardStep.SignIn, wizard.Step);
        Assert.False(wizard.AccountIsWrong);
    }

    [Fact]
    public async Task FailedLaunch_ShowsTheReasonAndDoesNotAdvance()
    {
        _service.OnLaunch = _ => new LaunchOutcome(LaunchOutcomeKind.Failed, "No se encontró Claude Desktop.", []);
        var wizard = await OpenWizardAsync();
        await wizard.NextCommand.ExecuteAsync();

        await wizard.OpenProfileCommand.ExecuteAsync();

        Assert.Equal("No se encontró Claude Desktop.", wizard.Error);
        Assert.False(wizard.CanGoNext);
    }

    [Fact]
    public async Task FinishedWizard_DoesNotShowAgain()
    {
        await _vm.InitializeAsync(NoArgs);
        _dialogs.DriveWizard = async wizard =>
        {
            await wizard.NextCommand.ExecuteAsync();
            await wizard.OpenProfileCommand.ExecuteAsync();
            await wizard.NextCommand.ExecuteAsync();
            wizard.AccountIsCorrect = true;
            var done = new TaskCompletionSource<bool>();
            wizard.Completed += (_, _) => done.TrySetResult(true);
            await wizard.NextCommand.ExecuteAsync();
            return done.Task.IsCompleted;
        };

        await _vm.OpenAsync(Item(_work.Id));
        Assert.Contains("ya tiene su cuenta", _vm.StatusMessage);

        _service.Statuses[_work.Id] = FakeProfileService.Closed();
        await _vm.OpenAsync(Item(_work.Id));

        Assert.Single(_dialogs.Wizards);
        Assert.Equal(2, _service.Launched.Count);
    }

    [Fact]
    public async Task AbandonedWizard_ShowsAgainNextTime()
    {
        await _vm.InitializeAsync(NoArgs);
        _dialogs.DriveWizard = async wizard =>
        {
            await wizard.NextCommand.ExecuteAsync();
            await wizard.OpenProfileCommand.ExecuteAsync();
            return false;
        };

        await _vm.OpenAsync(Item(_work.Id));
        _service.Statuses[_work.Id] = FakeProfileService.Closed();
        await _vm.RefreshAsync();
        await _vm.OpenAsync(Item(_work.Id));

        Assert.Equal(2, _dialogs.Wizards.Count);
        Assert.Empty(_service.LoginsConfirmed);
    }
}

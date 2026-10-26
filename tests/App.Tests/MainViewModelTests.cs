using System.IO;
using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.App.ViewModels;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Launch;

namespace ClaudeMultiAccount.App.Tests;

public class MainViewModelTests
{
    private static readonly StartupOptions NoArgs = new(LaunchProfile: null);

    private readonly FakeProfileService _service = new();
    private readonly FakeDialogService _dialogs = new(answer: true);
    private readonly MainViewModel _vm;

    public MainViewModelTests() => _vm = new MainViewModel(_service, _dialogs, () => new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc));

    private ProfileItemViewModel Item(Guid id) => _vm.Profiles.Single(p => p.Id == id);

    [Fact]
    public async Task Initialize_ListsProfilesWithNameInitialColorTypeAndState()
    {
        var main = TestProfiles.MainProfile();
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(main, pro);

        await _vm.InitializeAsync(NoArgs);

        Assert.Equal(new[] { "Claude Max", "Claude Pro" }, _vm.Profiles.Select(p => p.Name));
        var item = Item(pro.Id);
        Assert.Equal("P", item.Initial);
        Assert.Equal("#4A7FD9", item.Color);
        Assert.Equal("Desktop aislado", item.TypeLabel);
        Assert.Equal("Principal", Item(main.Id).TypeLabel);
        Assert.Equal(ProfileDisplayState.Closed, item.State);
        Assert.Equal("Cerrado", item.StateLabel);
        Assert.True(item.CanOpen);
        Assert.False(item.CanClose);
        Assert.Equal("Claude Desktop " + FakeProfileService.TestedVersion, _vm.ClaudeSummary);
        Assert.Empty(_vm.Notices);
        Assert.Null(_vm.EmptyMessage);
    }

    [Fact]
    public async Task OpenedOutsideTool_CanBeBroughtToFront_ButNotClosed()
    {
        var main = TestProfiles.MainProfile();
        _service.Document = TestProfiles.Document(main);
        _service.Statuses[main.Id] = FakeProfileService.RunningOutside();

        await _vm.InitializeAsync(NoArgs);
        var item = Item(main.Id);

        Assert.Equal(ProfileDisplayState.RunningOutsideTool, item.State);
        Assert.Contains("fuera de la herramienta", item.StateLabel);
        Assert.False(item.CanClose);
        Assert.False(item.CloseCommand.CanExecute(null));
        Assert.True(item.CanOpen);
        Assert.Equal("Traer al frente", item.OpenLabel);
        Assert.Contains("Salir", item.CloseToolTip);

        await _vm.CloseAsync(item);
        Assert.Empty(_dialogs.Asked);
        Assert.Empty(_service.CloseCalls);
    }

    [Fact]
    public async Task RunningFromTool_CanBeClosed()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.Statuses[pro.Id] = FakeProfileService.RunningFromTool(10, 11, 12);

        await _vm.InitializeAsync(NoArgs);
        var item = Item(pro.Id);

        Assert.Equal(ProfileDisplayState.Running, item.State);
        Assert.Equal("En ejecución", item.StateLabel);
        Assert.Equal(3, item.ProcessCount);
        Assert.True(item.CanClose);
        Assert.True(item.CloseCommand.CanExecute(null));
    }

    [Fact]
    public async Task StartingFromTool_ShowsStartingAndCanBeClosed()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.Statuses[pro.Id] = FakeProfileService.Starting(10);

        await _vm.InitializeAsync(NoArgs);

        Assert.Equal(ProfileDisplayState.Starting, Item(pro.Id).State);
        Assert.True(Item(pro.Id).CanClose);
    }

    [Fact]
    public async Task Refresh_PicksUpStateChanges()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        await _vm.InitializeAsync(NoArgs);
        var item = Item(pro.Id);
        Assert.Equal(ProfileDisplayState.Closed, item.State);

        _service.Statuses[pro.Id] = FakeProfileService.RunningFromTool(10);
        await _vm.RefreshAsync();

        Assert.Same(item, Item(pro.Id));
        Assert.Equal(ProfileDisplayState.Running, item.State);
    }

    [Fact]
    public async Task Refresh_AddsAndRemovesProfilesWithoutRecreatingRows()
    {
        var main = TestProfiles.MainProfile();
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(main);
        await _vm.InitializeAsync(NoArgs);
        var mainItem = Item(main.Id);

        _service.Document = TestProfiles.Document(pro, main);
        await _vm.RefreshAsync();

        Assert.Equal(new[] { pro.Id, main.Id }, _vm.Profiles.Select(p => p.Id));
        Assert.Same(mainItem, Item(main.Id));

        _service.Document = TestProfiles.Document(pro);
        await _vm.RefreshAsync();
        Assert.Equal(new[] { pro.Id }, _vm.Profiles.Select(p => p.Id));
    }

    [Fact]
    public async Task Open_LaunchesRecordsDateAndShowsRemovedVariables()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.OnLaunch = p => new LaunchOutcome(LaunchOutcomeKind.Launched, "'Claude Pro' abierto (Claude 2.42304.0.0).",
            ["Se quitaron del entorno de Claude estas variables (seguían en tu sistema): ANTHROPIC_API_KEY."], 1234);
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(pro.Id));

        Assert.Equal(new[] { pro.Id }, _service.Launched);
        Assert.Equal(pro.Id, Assert.Single(_service.Recorded).Id);
        Assert.Equal("'Claude Pro' abierto (Claude 2.42304.0.0).", _vm.StatusMessage);
        Assert.Contains(_vm.Notices, n => n.Contains("ANTHROPIC_API_KEY"));
        Assert.Equal(PendingOperation.None, Item(pro.Id).Pending);
    }

    [Fact]
    public async Task Open_ShowsStartingWhileLaunchIsInProgress_WithoutBlocking()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        using var release = new ManualResetEventSlim();
        _service.OnLaunch = p =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return new LaunchOutcome(LaunchOutcomeKind.Launched, "abierto", [], 1);
        };
        await _vm.InitializeAsync(NoArgs);
        var item = Item(pro.Id);

        var opening = _vm.OpenAsync(item);

        Assert.False(opening.IsCompleted);
        Assert.Equal(ProfileDisplayState.Starting, item.State);
        Assert.False(item.CanOpen);
        Assert.False(item.CanClose);

        release.Set();
        await opening;
        Assert.Equal(PendingOperation.None, item.Pending);
    }

    [Fact]
    public async Task Open_FailedLaunch_ShowsMessageAndDoesNotRecord()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.OnLaunch = _ => new LaunchOutcome(LaunchOutcomeKind.Failed, "No se encontró Claude Desktop.", []);
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(pro.Id));

        Assert.Equal("No se encontró Claude Desktop.", _vm.StatusMessage);
        Assert.Empty(_service.Recorded);
    }

    [Fact]
    public async Task Open_WhenSavingDateFails_StillReportsLaunchAndWarns()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.RecordThrows = true;
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(pro.Id));

        Assert.Equal("'Claude Pro' abierto.", _vm.StatusMessage);
        Assert.Contains(_vm.Notices, n => n.Contains("no se pudo guardar la fecha"));
    }

    [Fact]
    public async Task Open_ServiceThrows_WindowKeepsWorking()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.OnLaunch = _ => throw new IOException("disco lleno");
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(pro.Id));

        Assert.Contains("disco lleno", _vm.StatusMessage);
        Assert.Equal(PendingOperation.None, Item(pro.Id).Pending);
        Assert.True(Item(pro.Id).CanOpen);
    }

    [Fact]
    public async Task Close_AsksForExplicitConfirmationExplainingTrayAndDataLoss_ThenForces()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.Statuses[pro.Id] = FakeProfileService.RunningFromTool(10, 11);
        await _vm.InitializeAsync(NoArgs);

        await _vm.CloseAsync(Item(pro.Id));

        var (title, message) = Assert.Single(_dialogs.Asked);
        Assert.Contains("Claude Pro", title);
        Assert.Contains("bandeja", message);
        Assert.Contains("Salir", message);
        Assert.Contains("Se perderá lo que no esté guardado", message);
        Assert.Equal((pro.Id, true), Assert.Single(_service.CloseCalls));
        Assert.Equal("'Claude Pro' cerrado a la fuerza.", _vm.StatusMessage);
    }

    [Fact]
    public async Task Close_Declined_DoesNothing()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.Statuses[pro.Id] = FakeProfileService.RunningFromTool(10);
        _dialogs.Answer = false;
        await _vm.InitializeAsync(NoArgs);

        await _vm.CloseAsync(Item(pro.Id));

        Assert.Single(_dialogs.Asked);
        Assert.Empty(_service.CloseCalls);
        Assert.Contains("No se cerró", _vm.StatusMessage);
        Assert.Equal(ProfileDisplayState.Running, Item(pro.Id).State);
    }

    [Fact]
    public async Task Close_ShowsClosingWhileInProgress()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.Statuses[pro.Id] = FakeProfileService.RunningFromTool(10);
        using var release = new ManualResetEventSlim();
        _service.OnClose = (p, _) =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return new CloseOutcome(CloseOutcomeKind.Terminated, "cerrado");
        };
        await _vm.InitializeAsync(NoArgs);
        var item = Item(pro.Id);

        var closing = _vm.CloseAsync(item);

        Assert.Equal(ProfileDisplayState.Closing, item.State);
        Assert.Equal("Cerrando…", item.StateLabel);
        Assert.False(item.CanOpen);

        _service.Statuses[pro.Id] = FakeProfileService.Closed();
        release.Set();
        await closing;
        Assert.Equal(ProfileDisplayState.Closed, item.State);
    }

    [Fact]
    public async Task ClaudeNotFound_ShowsNotice()
    {
        _service.Document = TestProfiles.Document(TestProfiles.Pro());
        _service.Claude = new LocateResult(null, "No se encontró Claude Desktop (paquete MSIX Claude_pzs8sxrjxfjjc) para este usuario.");

        await _vm.InitializeAsync(NoArgs);

        Assert.Equal("Claude Desktop no encontrado", _vm.ClaudeSummary);
        Assert.Contains(_vm.Notices, n => n.StartsWith("No se encontró Claude Desktop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdatedClaude_ShowsTheUpdateBannerUntilChecked()
    {
        _service.Document = TestProfiles.Document(TestProfiles.Pro());
        await _vm.InitializeAsync(NoArgs);
        Assert.Null(_vm.UpdateBanner);
        Assert.Empty(_vm.Notices);

        _service.Claude = new LocateResult(FakeProfileService.Install(new Version(2, 50000, 0, 0)), null);
        await _vm.RefreshAsync();

        Assert.Contains("se actualizó de " + FakeProfileService.TestedVersion + " a 2.50000.0.0", _vm.UpdateBanner);
        Assert.Contains("no está en la lista de versiones probadas", _vm.UpdateBanner);

        await _vm.CheckCompatibilityAsync();

        Assert.True(Assert.Single(_dialogs.Reports).AllOk);
        Assert.Equal(new[] { new Version(2, 50000, 0, 0) }, _service.VersionsMarkedChecked);
        Assert.Null(_vm.UpdateBanner);
        Assert.Contains(_vm.Notices, n => n.Contains("la comprobación de este equipo fue bien"));
    }

    [Fact]
    public async Task FailedCheck_KeepsTheBanner()
    {
        _service.Document = TestProfiles.Document(TestProfiles.Pro());
        await _vm.InitializeAsync(NoArgs);
        _service.Claude = new LocateResult(FakeProfileService.Install(new Version(2, 50000, 0, 0)), null);
        _service.CompatibilityItems = [new("«Claude Pro»: datos de Claude Desktop", false, "no está")];
        await _vm.RefreshAsync();

        await _vm.CheckCompatibilityAsync();

        Assert.False(Assert.Single(_dialogs.Reports).AllOk);
        Assert.Empty(_service.VersionsMarkedChecked);
        Assert.NotNull(_vm.UpdateBanner);
    }

    [Fact]
    public async Task UpdateToATestedVersion_StillSuggestsChecking()
    {
        _service.Claude = new LocateResult(FakeProfileService.Install(new Version(2, 1, 0, 0)), null);
        await _vm.InitializeAsync(NoArgs);
        _service.Claude = new LocateResult(FakeProfileService.Install(FakeProfileService.TestedVersion), null);

        await _vm.RefreshAsync();

        Assert.Contains("Esta versión está en la lista de versiones probadas", _vm.UpdateBanner);
    }

    [Fact]
    public async Task TestedVersionsUnreadable_ShowsNotice()
    {
        _service.Document = TestProfiles.Document(TestProfiles.Pro());
        _service.Tested = null;

        await _vm.InitializeAsync(NoArgs);

        Assert.Contains(_vm.Notices, n => n.Contains("lista de versiones probadas"));
    }

    [Fact]
    public async Task OverridesInWindowsEnvironment_ShowsNamesOnly()
    {
        _service.Document = TestProfiles.Document(TestProfiles.Pro());
        _service.Overrides = ["ANTHROPIC_API_KEY", "ANTHROPIC_BASE_URL"];

        await _vm.InitializeAsync(NoArgs);

        var notice = Assert.Single(_vm.Notices);
        Assert.Contains("ANTHROPIC_API_KEY, ANTHROPIC_BASE_URL", notice);
    }

    [Fact]
    public async Task NoWindowsEnvironment_ShowsNotice()
    {
        _service.Document = TestProfiles.Document(TestProfiles.Pro());
        _service.Overrides = null;

        await _vm.InitializeAsync(NoArgs);

        Assert.Contains(_vm.Notices, n => n.Contains("no se podrá abrir ningún perfil"));
    }

    [Fact]
    public async Task MissingProfilesFile_ThatCannotBeCreated_ExplainsWhy()
    {
        _service.FileExists = false;
        _service.CreateThrows = true;

        await _vm.InitializeAsync(NoArgs);

        Assert.Empty(_vm.Profiles);
        Assert.Contains("No se pudo crear la lista de perfiles", _vm.EmptyMessage);
        Assert.Contains("sin permiso", _vm.StatusMessage);
    }

    [Fact]
    public async Task FirstRun_CreatesTheMainProfileAndWelcomes()
    {
        _service.FileExists = false;
        _service.CreatesFileOnInit = true;

        await _vm.InitializeAsync(NoArgs);

        var main = Assert.Single(_vm.Profiles);
        Assert.True(main.IsMain);
        Assert.Null(_vm.EmptyMessage);
        Assert.Contains("Nuevo perfil", _vm.StatusMessage);
    }

    [Fact]
    public async Task BrokenProfilesFile_KeepsLastGoodListAndShowsError()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        await _vm.InitializeAsync(NoArgs);

        _service.LoadError = "profiles.json no es JSON válido (línea 3).";
        _service.Statuses[pro.Id] = FakeProfileService.RunningFromTool(10);
        await _vm.RefreshAsync();

        Assert.Equal(ProfileDisplayState.Running, Item(pro.Id).State);
        Assert.Contains("profiles.json no es JSON válido (línea 3).", _vm.Notices);
    }

    [Fact]
    public async Task StatusFailure_MarksOnlyThatProfileAsError()
    {
        var main = TestProfiles.MainProfile();
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(main, pro);
        _service.StatusThrows.Add(pro.Id);

        await _vm.InitializeAsync(NoArgs);

        Assert.Equal(ProfileDisplayState.Error, Item(pro.Id).State);
        Assert.Contains("fallo simulado", Item(pro.Id).StateDetail);
        Assert.Equal(ProfileDisplayState.Closed, Item(main.Id).State);
    }

    [Fact]
    public async Task TransientReadFailure_KeepsLastKnownState()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.Statuses[pro.Id] = FakeProfileService.RunningFromTool(10);
        await _vm.InitializeAsync(NoArgs);

        _service.StatusThrowsIo.Add(pro.Id);
        _service.LoadThrowsIo = true;
        await _vm.RefreshAsync();

        Assert.Equal(ProfileDisplayState.Running, Item(pro.Id).State);
        Assert.Contains(_vm.Notices, n => n.Contains("archivo en uso"));

        _service.StatusThrowsIo.Clear();
        _service.LoadThrowsIo = false;
        await _vm.RefreshAsync();
        Assert.Empty(_vm.Notices);
    }

    [Fact]
    public async Task UnexpectedRefreshFailure_IsReportedAndRefreshKeepsWorking()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        await _vm.InitializeAsync(NoArgs);

        _service.ThrowOnLocate = true;
        await _vm.RefreshAsync();
        Assert.Contains("No se pudo actualizar el estado", _vm.StatusMessage);

        _service.ThrowOnLocate = false;
        _service.Statuses[pro.Id] = FakeProfileService.RunningFromTool(10);
        await _vm.RefreshAsync();
        Assert.Equal(ProfileDisplayState.Running, Item(pro.Id).State);
    }

    [Fact]
    public async Task UnknownLock_IsError()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        _service.Statuses[pro.Id] = FakeProfileService.LockUnknown();

        await _vm.InitializeAsync(NoArgs);

        Assert.Equal(ProfileDisplayState.Error, Item(pro.Id).State);
        Assert.Equal("Error", Item(pro.Id).StateLabel);
    }

    [Fact]
    public async Task LaunchArgument_OpensThatProfile()
    {
        var main = TestProfiles.MainProfile();
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(main, pro);

        await _vm.InitializeAsync(new StartupOptions(pro.Id.ToString()));

        Assert.Equal(new[] { pro.Id }, _service.Launched);
    }

    [Fact]
    public async Task LaunchArgument_UnknownProfile_ShowsMessage()
    {
        _service.Document = TestProfiles.Document(TestProfiles.Pro());

        await _vm.InitializeAsync(new StartupOptions("Claude Trabajo"));

        Assert.Empty(_service.Launched);
        Assert.Contains("Claude Trabajo", _vm.StatusMessage);
    }

    [Fact]
    public async Task InvalidColor_FallsBackToDefault_AndInitialComesFromName()
    {
        var pro = TestProfiles.Pro();
        pro.IconColor = "rojo";
        pro.IconGlyph = "";
        _service.Document = TestProfiles.Document(pro);

        await _vm.InitializeAsync(NoArgs);

        Assert.Equal(ProfileItemViewModel.DefaultColor, Item(pro.Id).Color);
        Assert.Equal("C", Item(pro.Id).Initial);
    }

    [Fact]
    public async Task RowText_IsSpanishForScreenReaders()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        await _vm.InitializeAsync(NoArgs);

        Assert.Equal("Claude Pro, Cerrado", Item(pro.Id).ToString());
    }

    [Fact]
    public async Task TrayMenu_ShowsStateAndOnlyAllowedActions()
    {
        var main = TestProfiles.MainProfile();
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(main, pro);
        _service.Statuses[main.Id] = FakeProfileService.RunningOutside();
        _service.Statuses[pro.Id] = FakeProfileService.RunningFromTool(10);
        await _vm.InitializeAsync(NoArgs);

        var menu = _vm.BuildTrayMenu();

        Assert.Equal(new[]
        {
            "Traer al frente «Claude Max» · Abierto fuera",
            "Traer al frente «Claude Pro» · Abierto",
            "Cerrar «Claude Pro»…",
        }, menu.Select(e => e.Text));
        Assert.True(menu[0].Enabled);
        Assert.True(menu[1].Enabled);
        Assert.DoesNotContain(menu, e => e.Text.Contains("Cerrar «Claude Max»"));
    }

    [Fact]
    public async Task TrayMenu_OpenAndCloseUseTheSameFlowAsTheWindow()
    {
        var pro = TestProfiles.Pro();
        _service.Document = TestProfiles.Document(pro);
        await _vm.InitializeAsync(NoArgs);

        await _vm.BuildTrayMenu().Single().Action!();
        Assert.Equal(new[] { pro.Id }, _service.Launched);

        _service.Statuses[pro.Id] = FakeProfileService.RunningFromTool(10);
        await _vm.RefreshAsync();
        await _vm.BuildTrayMenu().Single(e => e.Text.StartsWith("Cerrar", StringComparison.Ordinal)).Action!();

        Assert.Single(_dialogs.Asked);
        Assert.Equal((pro.Id, true), Assert.Single(_service.CloseCalls));
    }

    [Fact]
    public async Task TrayMenu_WithoutProfiles_SaysSo()
    {
        _service.FileExists = false;
        await _vm.InitializeAsync(NoArgs);

        var entry = Assert.Single(_vm.BuildTrayMenu());
        Assert.Equal("No hay perfiles", entry.Text);
        Assert.False(entry.Enabled);
    }

    [Fact]
    public void CloseConfirmation_NamesTheProfile()
    {
        var text = MainViewModel.BuildCloseConfirmation("Claude Pro");
        Assert.Contains("«Claude Pro»", text);
        Assert.EndsWith("¿Forzar el cierre de «Claude Pro»?", text);
    }

    [Fact]
    public void RefreshInterval_KeepsStateUnderThreeSeconds()
    {
        Assert.True(MainViewModel.RefreshInterval < TimeSpan.FromSeconds(3));
    }
}

public class StartupOptionsTests
{
    [Fact]
    public void Parse_Uninstall()
    {
        Assert.True(StartupOptions.Parse(["--uninstall"]).Uninstall);
        Assert.False(StartupOptions.Parse(["--launch", "x"]).Uninstall);
        Assert.Equal("x", StartupOptions.Parse(["--uninstall", "--launch", "x"]).LaunchProfile);
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "--launch", "abc" }, "abc")]
    [InlineData(new[] { "--LAUNCH", "Claude Pro" }, "Claude Pro")]
    [InlineData(new[] { "--launch=abc" }, "abc")]
    [InlineData(new[] { "--launch" }, null)]
    [InlineData(new[] { "--otra", "x", "--launch", "  id  " }, "id")]
    public void Parse(string[] args, string? expected)
    {
        Assert.Equal(expected, StartupOptions.Parse(args).LaunchProfile);
    }
}

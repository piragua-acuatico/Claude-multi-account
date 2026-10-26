using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Logging;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Processes;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Tests;

public class LaunchServiceTests
{
    private const string RedirectedPrueba =
        FakeEnvironment.LocalAppData + @"\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\CMA-prueba";
    private const string MainData = FakeEnvironment.AppData + @"\Claude";

    private readonly FakeEnvironment _env = new();
    private readonly FakeFileSystem _fs = new();
    private readonly FakeProcessHost _host = new();
    private readonly FakeLockProbe _lock = new();
    private readonly RuntimeStateStore _state;
    private readonly ProcessMonitor _monitor;
    private readonly LaunchService _service;

    public LaunchServiceTests()
    {
        _fs.Files[TestData.Install().ExePath] = "";
        _fs.Directories.Add(MainData);
        var query = new FakePackageQuery(new PackageInfo(TestData.Install().PackageFullName, TestData.InstallLocation));
        _state = new RuntimeStateStore(new AppPaths(@"C:\cma"), _fs);
        _monitor = new ProcessMonitor(_host, _lock, _state, new DataDirResolver(_env, _fs), _env);
        _service = new LaunchService(new InstallLocator(query, _fs), _monitor, _state, _host, _env, new NullLogger());
    }

    /// <summary>Simula un perfil lanzado antes por la herramienta, con su árbol de procesos vivo.</summary>
    private RootProcess LaunchedEarlier(Profile profile, params int[] pids)
    {
        var root = new RootProcess(pids[0], StartTimeFileTimeUtc: 42);
        _state.Set(profile.Id, root);
        _host.Trees[root] = pids.ToList();
        return root;
    }

    [Fact]
    public void Launch_Closed_StartsAndRemembersRoot()
    {
        var profile = TestData.Isolated();
        var outcome = _service.Launch(profile);

        Assert.Equal(LaunchOutcomeKind.Launched, outcome.Kind);
        var plan = Assert.Single(_host.Started);
        Assert.StartsWith("--user-data-dir=", plan.Arguments[0]);
        Assert.Equal(outcome.ProcessId, _state.Get(profile.Id)!.Pid);
        Assert.Equal(new[] { outcome.ProcessId!.Value }, _monitor.GetProcessIds(profile));
    }

    [Fact]
    public void Launch_WhenRunningFromTool_FocusesInsteadOfStarting()
    {
        var profile = TestData.Isolated();
        LaunchedEarlier(profile, 42, 43);
        _lock.States[RedirectedPrueba] = LockState.Held;

        var outcome = _service.Launch(profile);

        Assert.Equal(LaunchOutcomeKind.AlreadyRunning, outcome.Kind);
        Assert.Empty(_host.Started);
        Assert.Equal(new[] { 42, 43 }, _host.Focused);
        Assert.Contains("traído al frente", outcome.Message);
        // Con la ventana encontrada no se relanza Claude (Claude 3 avisaría «That session is no longer available»).
        Assert.Empty(_host.Activated);
        Assert.Equal(new[] { 42, 43 }, _monitor.GetProcessIds(profile));
    }

    [Fact]
    public void Launch_MainOpenedOutsideTool_FocusesTheOnlyClaudeThatIsNotOurs()
    {
        var pro = TestData.Isolated();
        LaunchedEarlier(pro, 42, 43);
        _host.ClaudeRoots.AddRange([42, 77]); // 42 es Pro (de la herramienta); 77, el principal abierto fuera.
        _lock.States[MainData] = LockState.Held;

        var outcome = _service.Launch(TestData.MainProfile());

        Assert.Equal(LaunchOutcomeKind.AlreadyRunning, outcome.Kind);
        Assert.Equal(new[] { 77 }, _host.Focused);
        Assert.Empty(_host.Activated);
        Assert.Empty(_host.Started);
    }

    [Fact]
    public void Launch_OpenedOutsideTool_WithoutAWindowToFind_AsksClaudeToShowItself()
    {
        _lock.States[MainData] = LockState.Held;
        var outcome = _service.Launch(TestData.MainProfile());

        Assert.Equal(LaunchOutcomeKind.AlreadyRunning, outcome.Kind);
        Assert.Empty(_host.Started);
        Assert.Empty(Assert.Single(_host.Activated).Arguments);
        Assert.Contains("Mostrando", outcome.Message);
        Assert.Null(_state.Get(TestData.MainProfile().Id));
    }

    [Fact]
    public void Launch_StaleLock_IsTreatedAsClosed()
    {
        _lock.States[RedirectedPrueba] = LockState.Stale;
        Assert.Equal(LaunchOutcomeKind.Launched, _service.Launch(TestData.Isolated()).Kind);
    }

    [Fact]
    public void Launch_WarnsAboutRemovedVariables()
    {
        _env.Variables["ANTHROPIC_API_KEY"] = "valor-secreto";
        var outcome = _service.Launch(TestData.Isolated());

        var warning = Assert.Single(outcome.Warnings);
        Assert.Contains("ANTHROPIC_API_KEY", warning);
        Assert.DoesNotContain("valor-secreto", warning);
    }

    [Fact]
    public void Launch_DoesNotPassVariablesFromTheConsoleSession()
    {
        // Caso real (2026-10-15): cma ejecutado desde una sesión de Claude Code de la cuenta Max.
        _env.ConsoleOnly["CLAUDE_CODE_USER_EMAIL"] = "max@ejemplo";
        _env.ConsoleOnly["CLAUDE_CODE_ACCOUNT_UUID"] = "uuid-max";
        _env.ConsoleOnly["CLAUDECODE"] = "1";
        _env.ConsoleOnly["CLAUDE_CONFIG_DIR"] = @"C:\otra";

        var outcome = _service.Launch(TestData.Isolated());

        Assert.Equal(LaunchOutcomeKind.Launched, outcome.Kind);
        var env = Assert.Single(_host.Started).Environment;
        Assert.DoesNotContain(env.Keys, k => k is "CLAUDE_CODE_USER_EMAIL" or "CLAUDE_CODE_ACCOUNT_UUID" or "CLAUDECODE");
        Assert.Equal(@"C:\Users\prueba\.claude-prueba", env["CLAUDE_CONFIG_DIR"]);
        Assert.Equal(@"C:\Windows\System32", env["PATH"]);
    }

    [Fact]
    public void Launch_WithoutCleanWindowsEnvironment_FailsClosed()
    {
        _env.DefaultEnvironmentAvailable = false;

        var outcome = _service.Launch(TestData.Isolated());

        Assert.Equal(LaunchOutcomeKind.Failed, outcome.Kind);
        Assert.Empty(_host.Started);
    }

    [Fact]
    public void Close_PoliteCloseSucceeds_AndForgetsRoot()
    {
        var profile = TestData.Isolated();
        LaunchedEarlier(profile, 7, 8);
        _lock.States[RedirectedPrueba] = LockState.Held;

        var outcome = _service.Close(profile, force: false, TimeSpan.FromSeconds(1));

        Assert.Equal(CloseOutcomeKind.Closed, outcome.Kind);
        Assert.Empty(_host.TerminatedPids);
        Assert.Null(_state.Get(profile.Id));
    }

    [Fact]
    public void Close_StillRunningWithoutForce_DoesNotKill()
    {
        var profile = TestData.Isolated();
        LaunchedEarlier(profile, 7);
        _host.ClosesOnRequest = false;

        var outcome = _service.Close(profile, force: false, TimeSpan.Zero);

        Assert.Equal(CloseOutcomeKind.StillRunning, outcome.Kind);
        Assert.Empty(_host.TerminatedPids);
        Assert.NotNull(_state.Get(profile.Id));
    }

    [Fact]
    public void Close_StillRunningWithForce_TerminatesOnlyThatTree()
    {
        var profile = TestData.Isolated();
        var other = TestData.Isolated("Otro", "otro");
        LaunchedEarlier(profile, 7, 8);
        LaunchedEarlier(other, 20, 21);
        _host.ClosesOnRequest = false;

        Assert.Equal(CloseOutcomeKind.Terminated, _service.Close(profile, force: true, TimeSpan.Zero).Kind);
        Assert.Equal(new[] { 7, 8 }, _host.TerminatedPids);
        Assert.Equal(new[] { 20, 21 }, _monitor.GetProcessIds(other));
    }

    [Fact]
    public void Close_OpenedOutsideTool_IsNotTouched()
    {
        _lock.States[MainData] = LockState.Held;
        Assert.Equal(CloseOutcomeKind.OpenedOutsideTool, _service.Close(TestData.MainProfile(), force: true, TimeSpan.Zero).Kind);
        Assert.Empty(_host.TerminatedPids);
    }

    [Fact]
    public void Status_DeadRootFromEarlierRun_CountsAsNoProcesses()
    {
        var profile = TestData.Isolated();
        // La raíz guardada ya no existe (o su PID es ahora de otro proceso).
        _state.Set(profile.Id, new RootProcess(99, 1));
        Assert.Equal(ProfileState.Closed, _monitor.GetStatus(profile, TestData.Install()).State);
    }

    [Fact]
    public void Status_StartingWhenTreeAliveButNoLock()
    {
        var profile = TestData.Isolated();
        LaunchedEarlier(profile, 1);
        Assert.Equal(ProfileState.Starting, _monitor.GetStatus(profile, TestData.Install()).State);
    }
}

public class WindowsProcessHostTests
{
    [Fact]
    public void ProcessThatExited_IsNotAlive_EvenWhileSomeoneHoldsItsHandle()
    {
        // T-02: un antivirus mantenía abierta la raíz de Pro ya terminada y la herramienta la daba por viva.
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cmd, "/d /c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var root = new RootProcess(process.Id, process.StartTime.ToFileTimeUtc());
        Assert.True(process.WaitForExit(10_000));

        // «process» sigue abierto: el proceso terminado aún se puede abrir, como en el caso real.
        Assert.Null(WindowsProcessHost.TryGetStartTime(root.Pid));
        Assert.Null(new WindowsProcessHost().GetProcessTree(root));
    }

    [Fact]
    public void LiveProcess_IsAlive()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var root = new RootProcess(current.Id, current.StartTime.ToFileTimeUtc());
        Assert.Equal(root.StartTimeFileTimeUtc, WindowsProcessHost.TryGetStartTime(root.Pid));
        Assert.Contains(current.Id, new WindowsProcessHost().GetProcessTree(root)!);
    }
}

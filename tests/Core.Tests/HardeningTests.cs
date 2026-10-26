using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Logging;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Processes;

namespace ClaudeMultiAccount.Core.Tests;

/// <summary>Comprobaciones de endurecimiento (SECURITY.md).</summary>
public class HardeningTests
{
    /// <summary>
    /// La herramienta no se conecta a nada (SECURITY.md §8: sin telemetría). Si algún día hace falta la red
    /// (p. ej. comprobar versiones nuevas de la propia herramienta, opcional y apagado), se decide aquí a la vista.
    /// </summary>
    [Fact]
    public void SourceCode_DoesNotUseTheNetwork()
    {
        string[] forbidden = ["HttpClient", "WebClient", "WebRequest", "System.Net.Sockets", "TcpClient", "UdpClient", "Dns.", "System.Net.Http"];
        var src = Path.Combine(ForbiddenReferencesTests.FindRepoRoot(), "src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => forbidden.Where(File.ReadAllText(f).Contains).Select(n => $"{Path.GetRelativePath(src, f)} → {n}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Uso de red en src/:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Lines { get; } = [];
        public void Info(string message) => Lines.Add(message);
        public void Warn(string message) => Lines.Add(message);
        public void Error(string message, Exception? exception = null) => Lines.Add(message + " " + exception?.Message);
    }

    /// <summary>T8: ni los avisos ni el registro llevan valores de variables de entorno, solo sus nombres.</summary>
    [Fact]
    public void Launch_NeverLogsEnvironmentValues()
    {
        var env = new FakeEnvironment();
        var fs = new FakeFileSystem();
        var host = new FakeProcessHost();
        var log = new RecordingLogger();
        fs.Files[TestData.Install().ExePath] = "";
        var query = new FakePackageQuery(new PackageInfo(TestData.Install().PackageFullName, TestData.InstallLocation));
        var state = new RuntimeStateStore(new AppPaths(@"C:\cma"), fs);
        var monitor = new ProcessMonitor(host, new FakeLockProbe(), state, new DataDirResolver(env, fs), env);
        var service = new LaunchService(new InstallLocator(query, fs), monitor, state, host, env, log);

        env.Variables["ANTHROPIC_API_KEY"] = "sk-ant-valor-secreto-1";
        env.Variables["ANTHROPIC_BASE_URL"] = "https://proxy-secreto.example";
        env.ConsoleOnly["CLAUDE_CODE_USER_EMAIL"] = "correo-secreto@example.com";
        env.ConsoleOnly["CLAUDE_CODE_SESSION_ID"] = "sesion-secreta";

        var outcome = service.Launch(TestData.Isolated());
        service.Close(TestData.Isolated(), force: true, TimeSpan.Zero);

        var everything = string.Join("\n", log.Lines.Concat(outcome.Warnings).Append(outcome.Message));
        Assert.Contains("ANTHROPIC_API_KEY", everything);
        foreach (var secret in new[] { "valor-secreto", "proxy-secreto", "correo-secreto", "sesion-secreta" })
        {
            Assert.DoesNotContain(secret, everything);
        }
    }

    [Fact]
    public void Redactor_HidesKeysEvenIfSomethingSlipsThrough()
    {
        var line = Redactor.Redact("error con sk-ant-api03-ABCDEFGHIJKLMNOP en C:\\Users\\prueba\\x", @"C:\Users\prueba");
        Assert.DoesNotContain("ABCDEFGHIJKLMNOP", line);
        Assert.Contains("%USERPROFILE%", line);
    }
}

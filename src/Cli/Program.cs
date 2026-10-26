using System.Reflection;
using System.Text;
using System.Text.Json;
using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Launch;
using ClaudeMultiAccount.Core.Logging;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Processes;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Cli;

internal static class Program
{
    private const int Ok = 0;
    private const int Failure = 1;
    private const int Usage = 2;

    private static int Main(string[] args)
    {
        // UTF-8 para las tildes; se devuelve la página de códigos de la consola al terminar.
        var previousEncoding = Console.OutputEncoding;
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return new CliApp().Run(args);
        }
        catch (Exception ex) when (ex is ProfileStoreException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return Failure;
        }
        finally
        {
            Console.OutputEncoding = previousEncoding;
        }
    }

    private sealed class CliApp
    {
        private readonly IEnvironment _env = new SystemEnvironment();
        private readonly IFileSystem _fs = new PhysicalFileSystem();
        private readonly AppPaths _paths;
        private readonly ProfileStore _store;
        private readonly InstallLocator _locator;
        private readonly DataDirResolver _resolver;
        private readonly IProcessHost _host = new WindowsProcessHost();
        private readonly RuntimeStateStore _runtimeState;
        private readonly ProcessMonitor _monitor;
        private readonly ILogger _log;
        private readonly string? _userProfile;

        public CliApp()
        {
            _paths = AppPaths.FromEnvironment(_env);
            _store = new ProfileStore(_paths, _fs, _env);
            _locator = new InstallLocator(new AppxPackageQuery(), _fs);
            _resolver = new DataDirResolver(_env, _fs);
            _runtimeState = new RuntimeStateStore(_paths, _fs);
            _monitor = new ProcessMonitor(_host, new LockfileProbe(), _runtimeState, _resolver, _env);
            _log = new FileLogger(_paths.LogsDir);
            _userProfile = _env.Get("USERPROFILE");
        }

        public int Run(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return Usage;
            }

            var command = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToArray();
            string[] options = command switch
            {
                "close" => ["--forzar", "--force"],
                "init" => ["--simular", "--dry-run"],
                _ => [],
            };
            if (command is not ("help" or "--help" or "-h" or "/?") && rest.FirstOrDefault(a => IsOption(a) && !options.Contains(a, StringComparer.OrdinalIgnoreCase)) is { } unknown)
            {
                Console.Error.WriteLine($"Opción desconocida para '{command}': {unknown}");
                PrintHelp();
                return Usage;
            }
            var positional = rest.Where(a => !IsOption(a)).ToArray();
            var flags = rest.Where(IsOption).Select(a => a.ToLowerInvariant()).ToHashSet();

            return command switch
            {
                "list" => NoArguments(positional) ?? List(),
                "status" => NoArguments(positional) ?? Status(),
                "launch" => Launch(positional),
                "close" => Close(positional, force: flags.Overlaps(["--forzar", "--force"])),
                "diagnose" => NoArguments(positional) ?? Diagnose(),
                "init" => NoArguments(positional) ?? Init(simulate: flags.Overlaps(["--simular", "--dry-run"])),
                "help" or "--help" or "-h" or "/?" => Help(),
                _ => Unknown(command),
            };
        }

        private static bool IsOption(string arg) => arg.StartsWith('-') || arg.StartsWith('/');

        private static int? NoArguments(string[] positional)
        {
            if (positional.Length == 0)
            {
                return null;
            }
            Console.Error.WriteLine($"Este comando no lleva argumentos: {string.Join(' ', positional)}");
            return Usage;
        }

        private int Help()
        {
            PrintHelp();
            return Ok;
        }

        private int Unknown(string command)
        {
            Console.Error.WriteLine($"Comando desconocido: {command}");
            PrintHelp();
            return Usage;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("""
                Claude Multi-Account (cma) — lanza el Claude Desktop oficial con un perfil por cuenta.

                Uso:
                  cma list                         Lista los perfiles y su estado.
                  cma status                       Estado de Claude y de cada perfil.
                  cma launch <perfil>              Abre el perfil (nombre o id).
                  cma close <perfil> [--forzar]    Cierra el perfil. --forzar termina sus procesos
                                                   si no se cierra solo (se pierde lo no guardado).
                  cma diagnose                     Informe para soporte (sin secretos ni valores de entorno).
                  cma init [--simular]             Crea profiles.json con el perfil principal (lo hace también
                                                   Claude Multi-Account al abrirse). --simular solo lo muestra.

                Los perfiles nuevos se crean en Claude Multi-Account: su primer inicio de sesión necesita el asistente.
                """);
        }

        private ProfileDocument LoadOrWarn()
        {
            if (!_store.Exists)
            {
                Console.WriteLine($"Todavía no hay perfiles ({Redact(_store.FilePath)}). Abre Claude Multi-Account o ejecuta 'cma init'.");
            }
            return _store.Load();
        }

        private int List()
        {
            var document = LoadOrWarn();
            var install = _locator.Locate().Install;
            foreach (var profile in document.Profiles)
            {
                var status = _monitor.GetStatus(profile, install);
                Console.WriteLine($"{profile.Name,-28} {TypeLabel(profile.Type),-16} {StateLabel(status)}");
            }
            return Ok;
        }

        private int Status()
        {
            var located = _locator.Locate();
            Console.WriteLine(located.Found
                ? $"Claude Desktop: {located.Install!.Version} (MSIX)"
                : $"Claude Desktop: no encontrado. {located.Error}");
            Console.WriteLine();

            var document = LoadOrWarn();
            foreach (var profile in document.Profiles)
            {
                var status = _monitor.GetStatus(profile, located.Install);
                Console.WriteLine($"{profile.Name,-28} {StateLabel(status),-36} procesos: {status.ProcessIds.Count}");
            }
            return Ok;
        }

        private int Launch(string[] args)
        {
            if (!TryFindProfile(args, out var profile))
            {
                return Usage;
            }
            if (profile.NeedsLoginWizard)
            {
                // Abrirlo aquí se saltaría la comprobación de la cuenta y el asistente ya no volvería a salir.
                Console.Error.WriteLine($"'{profile.Name}' todavía no tiene sesión iniciada: ábrelo la primera vez desde Claude Multi-Account, que te guía con el asistente.");
                return Failure;
            }

            var service = CreateLaunchService();
            var outcome = service.Launch(profile);
            foreach (var warning in outcome.Warnings)
            {
                Console.WriteLine("Aviso: " + warning);
            }

            if (outcome.Kind == LaunchOutcomeKind.Failed)
            {
                Console.Error.WriteLine(outcome.Message);
                return Failure;
            }

            Console.WriteLine(outcome.Message);
            if (outcome.Kind == LaunchOutcomeKind.Launched)
            {
                try
                {
                    new ProfileCatalog(_store).RecordLaunch(profile.Id, DateTime.UtcNow);
                }
                catch (Exception ex) when (ex is ProfileStoreException or IOException or UnauthorizedAccessException)
                {
                    // Claude ya está abierto: no apuntar la fecha no es motivo para fallar.
                    Console.WriteLine("Aviso: no se pudo apuntar la fecha de apertura: " + ex.Message);
                }
            }
            return Ok;
        }

        private int Close(string[] names, bool force)
        {
            if (!TryFindProfile(names, out var profile))
            {
                return Usage;
            }

            var outcome = CreateLaunchService().Close(profile, force, TimeSpan.FromSeconds(10));
            var failed = outcome.Kind is CloseOutcomeKind.StillRunning or CloseOutcomeKind.OpenedOutsideTool;
            (failed ? Console.Error : Console.Out).WriteLine(outcome.Message);
            return failed ? Failure : Ok;
        }

        private int Diagnose()
        {
            var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Console.WriteLine($"Herramienta: {version}");
            Console.WriteLine($"Windows: {Environment.OSVersion.Version}");

            var located = _locator.Locate();
            if (located.Found)
            {
                var i = located.Install!;
                Console.WriteLine($"Claude Desktop: MSIX {i.Version} · familia {i.PackageFamilyName}");
                Console.WriteLine($"Ejecutable: {Redact(i.ExePath)}");
            }
            else
            {
                Console.WriteLine($"Claude Desktop: no encontrado. {located.Error}");
            }

            // Solo nombres, nunca valores.
            var windowsEnv = _env.GetDefaultUserEnvironment();
            if (windowsEnv is null)
            {
                Console.WriteLine("Entorno de Windows: no se pudo obtener (no se podrá lanzar ningún perfil).");
            }
            else
            {
                var overrides = EnvScrubber.OverrideVariables.Where(windowsEnv.ContainsKey).ToList();
                Console.WriteLine("Variables que anulan la suscripción en tu entorno de Windows: " +
                                  (overrides.Count == 0 ? "ninguna" : string.Join(", ", overrides) + " (se quitan al lanzar)"));

                var consoleOnly = _env.GetAll().Keys
                    .Where(n => IsClaudeVariable(n) && !windowsEnv.ContainsKey(n))
                    .Count();
                if (consoleOnly > 0)
                {
                    Console.WriteLine($"Esta consola tiene {consoleOnly} variable(s) de una sesión de Claude que no se pasan a los perfiles.");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"profiles.json: {Redact(_store.FilePath)} ({(_store.Exists ? "existe" : "no existe")})");
            if (!_store.Exists)
            {
                return Ok;
            }

            ProfileDocument document;
            try
            {
                document = _store.Load();
            }
            catch (ProfileStoreException ex)
            {
                // Justo cuando más falta hace el informe: se dice qué pasa y no se corta.
                Console.WriteLine("No se pudo leer profiles.json: " + ex.Message);
                return Ok;
            }
            var errors = ProfileValidator.Validate(document, _env);
            Console.WriteLine(errors.Count == 0 ? "Validación: correcta" : "Validación: " + errors.Count + " error(es)");
            foreach (var error in errors)
            {
                Console.WriteLine("  - " + error);
            }

            foreach (var profile in document.Profiles)
            {
                var status = _monitor.GetStatus(profile, located.Install);
                Console.WriteLine();
                Console.WriteLine($"[{profile.Name}] {TypeLabel(profile.Type)} · {StateLabel(status)}");
                if (status.EffectiveDataDir is not null)
                {
                    Console.WriteLine($"  Datos de Desktop: {Redact(status.EffectiveDataDir)} (existe: {SiNo(_fs.DirectoryExists(status.EffectiveDataDir))})");
                    Console.WriteLine($"  lockfile: {LockLabel(status.Lock)}");
                }
                if (!string.IsNullOrWhiteSpace(profile.ClaudeConfigDir))
                {
                    var config = PathUtil.Expand(profile.ClaudeConfigDir, _env);
                    Console.WriteLine($"  Config. de Claude Code: {Redact(config)} (existe: {SiNo(_fs.DirectoryExists(config))})");
                }
                Console.WriteLine($"  Procesos lanzados por cma: {status.ProcessIds.Count}");
            }
            return Ok;
        }

        private int Init(bool simulate)
        {
            if (_store.Exists)
            {
                Console.Error.WriteLine($"Ya existe {Redact(_store.FilePath)}; no se sobrescribe.");
                return Failure;
            }

            var document = ProfileCatalog.CreateInitialDocument();
            if (simulate)
            {
                Console.WriteLine($"Se crearía {Redact(_store.FilePath)} con:");
                Console.WriteLine(JsonSerializer.Serialize(document, ProfileStore.JsonOptions));
                return Ok;
            }

            _store.Save(document);
            _log.Info($"profiles.json creado con {document.Profiles.Count} perfil(es).");
            Console.WriteLine($"Creado {Redact(_store.FilePath)}:");
            foreach (var profile in document.Profiles)
            {
                Console.WriteLine($"  - {profile.Name} ({TypeLabel(profile.Type)})");
            }
            return Ok;
        }

        private bool TryFindProfile(string[] args, out Profile profile)
        {
            var document = _store.Load();
            profile = null!;
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Indica el perfil (nombre o id). Usa 'cma list' para verlos.");
                return false;
            }

            var found = document.Find(string.Join(' ', args));
            if (found is null)
            {
                Console.Error.WriteLine($"No existe el perfil '{string.Join(' ', args)}'. Usa 'cma list' para verlos.");
                return false;
            }
            profile = found;
            return true;
        }

        private LaunchService CreateLaunchService() => new(_locator, _monitor, _runtimeState, _host, _env, _log);

        private string Redact(string text) => Redactor.Redact(text, _userProfile);

        private static string SiNo(bool value) => value ? "sí" : "no";

        private static bool IsClaudeVariable(string name) =>
            name.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase);

        private static string TypeLabel(ProfileType type) => type switch
        {
            ProfileType.Main => "Principal",
            ProfileType.DesktopIsolated => "Desktop aislado",
            ProfileType.Cli => "Terminal",
            _ => "?",
        };

        private static string StateLabel(ProfileStatus status) => status.State switch
        {
            ProfileState.Closed => "Cerrado",
            ProfileState.Starting => "Iniciando",
            ProfileState.Running when status.OpenedOutsideTool => "En ejecución (abierto fuera de cma)",
            ProfileState.Running => "En ejecución",
            _ => "Error",
        };

        private static string LockLabel(LockState state) => state switch
        {
            LockState.Absent => "no existe",
            LockState.Held => "en uso",
            LockState.Stale => "existe pero nadie lo usa",
            _ => "no se pudo comprobar",
        };
    }
}

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.App.ViewModels;

namespace ClaudeMultiAccount.App;

public partial class App : Application
{
    /// <summary>Identidad de la herramienta en la barra de tareas (agrupa sus ventanas y usa su propio icono).</summary>
    public const string AppUserModelId = "ClaudeMultiAccount.App";

    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Antes de crear ninguna ventana. Si falla, solo cambia cómo la agrupa la barra de tareas.
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        base.OnStartup(e);
        BrandAccent.Apply(this);
        WindowFit.Register();

        var options = StartupOptions.Parse(e.Args);
        if (options.Uninstall)
        {
            RunUninstall();
            return;
        }

        // Si ya está abierta (p. ej. en la bandeja), se le pasa el encargo y este arranque termina.
        var instanceName = SingleInstance.DefaultName;
        _instance = SingleInstance.TryAcquire(instanceName);
        if (_instance is null)
        {
            if (!SingleInstance.TrySend(instanceName, InstanceMessage.From(options)))
            {
                MessageWindow.Inform("Claude Multi-Account no responde",
                    "Claude Multi-Account ya está abierto, pero no responde. Ciérralo desde el icono de la " +
                    "bandeja («Salir») o desde el Administrador de tareas y vuelve a intentarlo.");
            }
            Shutdown();
            return;
        }

        IProfileService service;
        try
        {
            service = new CoreProfileService();
        }
        catch (InvalidOperationException ex)
        {
            // P. ej. sin LOCALAPPDATA: no hay dónde leer los perfiles.
            MessageWindow.Inform("Claude Multi-Account no puede arrancar", ex.Message, isError: true);
            Shutdown(1);
            return;
        }

        // Red de seguridad: un error inesperado se apunta y se avisa, pero no cierra la herramienta (ni ningún Claude).
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            service.LogUnexpected(args.Exception);
            try
            {
                MessageWindow.Inform("Algo salió mal",
                    "Ocurrió un error inesperado. La herramienta sigue funcionando y lo apuntó en su registro." +
                    Environment.NewLine + Environment.NewLine + args.Exception.Message, isError: true);
            }
            catch (InvalidOperationException)
            {
                // Si ni siquiera se puede mostrar el aviso, queda el registro.
            }
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            service.LogUnexpected(args.Exception);
            args.SetObserved();
        };

        var viewModel = new MainViewModel(service, new MessageBoxDialogService());
        var window = new MainWindow(viewModel, options);
        MainWindow = window;
        _instance.Listen(message => Dispatcher.InvokeAsync(() => window.HandleMessageAsync(message)));

        // Desde un acceso directo (--launch) la herramienta se queda en la bandeja y solo abre el perfil.
        if (options.LaunchProfile is null)
        {
            window.Show();
        }
        _ = window.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Tras instalar: suelta la instancia única, abre la versión instalada y cierra esta.</summary>
    public void HandOverTo(string installedExe)
    {
        _instance?.Dispose();
        _instance = null;
        SetupService.StartInstalled(installedExe);
        ((MainWindow)MainWindow).ExitApp();
    }

    /// <summary>
    /// Desinstalar. La herramienta no puede estar abierta a la vez: si lo está, se pide salir de ella.
    /// </summary>
    private void RunUninstall()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        using var instance = SingleInstance.TryAcquire(SingleInstance.DefaultName);
        if (instance is null)
        {
            MessageWindow.Inform("Cierra primero Claude Multi-Account",
                "Claude Multi-Account está abierto. Sal de él con «Salir» en su icono de la bandeja y vuelve a desinstalar.");
            Shutdown();
            return;
        }

        try
        {
            var setup = new SetupService();
            var plan = setup.PlanUninstall();
            var viewModel = new UninstallViewModel(plan, setup.Display);
            if (new UninstallWindow(viewModel).ShowDialog() == true)
            {
                setup.Uninstall(plan, viewModel.DeleteToolData);
                MessageWindow.Done("Claude Multi-Account desinstalado",
                    viewModel.DeleteToolData
                        ? "Se quitó la herramienta y su configuración. Tus Claude y sus carpetas siguen como estaban."
                        : "Se quitó la herramienta. Tu lista de perfiles y tus copias se conservan por si vuelves a instalarla; tus Claude siguen como estaban.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.Security.SecurityException)
        {
            MessageWindow.Inform("No se pudo desinstalar del todo", ex.Message, isError: true);
        }
        Shutdown();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}

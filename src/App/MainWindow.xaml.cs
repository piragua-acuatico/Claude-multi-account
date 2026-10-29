using System.ComponentModel;
using System.IO;
using System.Windows;
using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.App.Tray;
using ClaudeMultiAccount.App.ViewModels;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Setup;

namespace ClaudeMultiAccount.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly StartupOptions _options;
    private readonly CancellationTokenSource _refreshCancellation = new();
    private readonly AppIcon _appIcon = new();
    private readonly TrayIconController _tray;
    private bool _exiting;

    public MainWindow(MainViewModel viewModel, StartupOptions options)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _options = options;
        DataContext = viewModel;
        Icon = _appIcon.ToImageSource();
        _tray = new TrayIconController(viewModel, _appIcon, ShowFromTray, Exit, () => IsVisible);
        viewModel.MessageArrived += _tray.ShowMessage;
        InstallButton.Visibility = new SetupService().CanInstall ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Carga los perfiles (y abre el de --launch, si lo hay) y vigila su estado hasta salir.</summary>
    public async Task StartAsync()
    {
        await _viewModel.InitializeAsync(_options);
        ReportIfHidden();
        await _viewModel.RunAutoRefreshAsync(_refreshCancellation.Token);
    }

    /// <summary>Encargo de otro arranque: un acceso directo (abrir perfil) o abrir la herramienta otra vez (mostrar).</summary>
    public async Task HandleMessageAsync(InstanceMessage message)
    {
        if (message.LaunchProfile is { } profile)
        {
            await _viewModel.LaunchRequestedAsync(profile);
            ReportIfHidden();
        }
        else
        {
            ShowFromTray();
        }
    }

    private void ReportIfHidden()
    {
        if (!IsVisible && !string.IsNullOrWhiteSpace(_viewModel.StatusMessage))
        {
            _tray.ShowMessage(_viewModel.StatusMessage);
        }
    }

    /// <summary>«⋯ Más acciones» de cada perfil: abre su menú debajo del botón (también con el teclado).</summary>
    private void OnMoreActions(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.DataContext = button.DataContext;
            menu.IsOpen = true;
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    /// <summary>Cierra la herramienta (no la esconde). Los Claude abiertos siguen abiertos.</summary>
    public void ExitApp() => Exit();

    /// <summary>
    /// «Instalar en este equipo…»: solo en la versión empaquetada. Explica qué se crea, instala y ofrece
    /// abrir la versión instalada.
    /// </summary>
    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        SetupService setup;
        InstallPlan plan;
        try
        {
            setup = new SetupService();
            plan = setup.PlanInstall();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.Security.SecurityException or ProfileStoreException)
        {
            MessageWindow.Inform("No se pudo instalar", ex.Message, isError: true);
            return;
        }
        if (!MessageWindow.Confirm("Instalar Claude Multi-Account",
                "Se instalará solo para tu usuario, sin pedir permisos de administrador:" + Environment.NewLine +
                $"   • La herramienta en {setup.Display(plan.InstallDir)}" + Environment.NewLine +
                "   • Una entrada en el menú Inicio y otra en «Aplicaciones instaladas» de Windows" + Environment.NewLine +
                "   • Tus accesos directos de perfil pasarán a abrir la versión instalada" + Environment.NewLine + Environment.NewLine +
                "No se toca Claude Desktop ni tus perfiles. Para desinstalar, usa «Aplicaciones instaladas»." +
                Environment.NewLine + Environment.NewLine + "¿Instalar?"))
        {
            return;
        }

        try
        {
            var updated = await Task.Run(() => setup.Install(plan));
            if (MessageWindow.ConfirmAfterDone("Claude Multi-Account instalado",
                    $"Instalado en {setup.Display(plan.InstallDir)}." +
                    (updated > 0 ? $" Se actualizaron {updated} acceso(s) directo(s) de perfil." : "") +
                    Environment.NewLine + Environment.NewLine + "¿Abrir ahora la versión instalada? Esta copia se cerrará."))
            {
                ((App)Application.Current).HandOverTo(plan.ExePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.Security.SecurityException or ProfileStoreException)
        {
            MessageWindow.Inform("No se pudo instalar", ex.Message, isError: true);
        }
    }

    /// <summary>«Salir» del icono de la bandeja: cierra la herramienta. Los Claude abiertos siguen abiertos.</summary>
    private void Exit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            // La X esconde la ventana: la herramienta sigue en la bandeja para abrir y cerrar perfiles.
            e.Cancel = true;
            Hide();
            _tray.ShowHiddenHint();
            return;
        }

        // Cerrar la herramienta no cierra ningún Claude: solo deja de vigilarlos.
        _refreshCancellation.Cancel();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Dispose();
        _appIcon.Dispose();
        base.OnClosed(e);
    }
}

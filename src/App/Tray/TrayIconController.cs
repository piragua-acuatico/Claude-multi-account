using ClaudeMultiAccount.App.ViewModels;
using Forms = System.Windows.Forms;

namespace ClaudeMultiAccount.App.Tray;

/// <summary>
/// Icono de la herramienta en la bandeja del sistema. Su menú se rehace al abrirlo, con el estado
/// actual de cada perfil. Doble clic muestra la ventana. «Salir» cierra solo esta herramienta, nunca un Claude.
/// </summary>
internal sealed class TrayIconController : IDisposable
{
    private const string AppName = "Claude Multi-Account";

    private readonly MainViewModel _viewModel;
    private readonly Action _showWindow;
    private readonly Action _exit;
    private readonly Func<bool> _isWindowVisible;
    private readonly AppIcon _appIcon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly System.Drawing.Font _titleFont;
    private bool _hiddenHintShown;

    public TrayIconController(MainViewModel viewModel, AppIcon appIcon, Action showWindow, Action exit, Func<bool> isWindowVisible)
    {
        _viewModel = viewModel;
        _appIcon = appIcon;
        _showWindow = showWindow;
        _exit = exit;
        _isWindowVisible = isWindowVisible;
        _titleFont = new System.Drawing.Font(_menu.Font, System.Drawing.FontStyle.Bold);

        _menu.Opening += (_, e) =>
        {
            RebuildMenu();
            // Con los colores del modo de Windows de ese momento (claro u oscuro).
            FluentMenuRenderer.Apply(_menu);
            // Un ContextMenuStrip vacío llega con Cancel = true; tras rehacerlo sí tiene elementos.
            e.Cancel = false;
        };
        _menu.Opened += (_, _) => FluentMenuRenderer.RoundCorners(_menu);
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _appIcon.Icon,
            Text = AppName,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _showWindow();
            }
        };
    }

    /// <summary>La primera vez que la ventana se esconde, explica dónde quedó la herramienta y cómo salir.</summary>
    public void ShowHiddenHint()
    {
        if (_hiddenHintShown)
        {
            return;
        }
        _hiddenHintShown = true;
        _notifyIcon.ShowBalloonTip(6000, AppName,
            "Sigue aquí, en la bandeja. Para abrir o cerrar perfiles haz clic derecho en este icono; " +
            "para cerrar la herramienta, elige «Salir».", Forms.ToolTipIcon.Info);
    }

    /// <summary>Aviso del icono con el resultado de una acción hecha con la ventana escondida.</summary>
    public void ShowMessage(string text) =>
        _notifyIcon.ShowBalloonTip(5000, AppName, text, Forms.ToolTipIcon.Info);

    private void RebuildMenu()
    {
        var old = _menu.Items.Cast<Forms.ToolStripItem>().ToList();
        _menu.Items.Clear();
        foreach (var item in old)
        {
            item.Dispose();
        }

        _menu.Items.Add(new Forms.ToolStripLabel(AppName) { Font = _titleFont, Padding = new Forms.Padding(8, 4, 8, 4) });
        _menu.Items.Add(new Forms.ToolStripSeparator());

        // Con un diálogo abierto (p. ej. una confirmación) no se lanza otra acción encima: primero hay que responderlo.
        var dialogOpen = System.Windows.Interop.ComponentDispatcher.IsThreadModal;
        if (dialogOpen)
        {
            _menu.Items.Add(new Forms.ToolStripMenuItem("Hay una ventana esperando tu respuesta") { Enabled = false });
        }

        foreach (var entry in dialogOpen ? [] : _viewModel.BuildTrayMenu())
        {
            var menuItem = new Forms.ToolStripMenuItem(entry.Text) { Enabled = entry.Enabled && entry.Action is not null };
            if (entry.Action is { } action)
            {
                menuItem.Click += async (_, _) => await RunAsync(action);
            }
            _menu.Items.Add(menuItem);
        }

        _menu.Items.Add(new Forms.ToolStripSeparator());
        var show = new Forms.ToolStripMenuItem("Mostrar la ventana");
        show.Click += (_, _) => _showWindow();
        _menu.Items.Add(show);
        var exit = new Forms.ToolStripMenuItem("Salir (no cierra ningún Claude)");
        exit.Click += (_, _) => _exit();
        _menu.Items.Add(exit);
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            // Este clic llega por Windows Forms: un error aquí cerraría la herramienta en vez de llegar a su red de seguridad.
            ShowMessage("No se pudo completar: " + ex.Message);
            return;
        }
        // Con la ventana escondida, el resultado se ve en un aviso del icono.
        if (!_isWindowVisible() && !string.IsNullOrWhiteSpace(_viewModel.StatusMessage))
        {
            ShowMessage(_viewModel.StatusMessage);
        }
    }

    public void Dispose()
    {
        // Sin esto el icono se queda "fantasma" en la bandeja hasta pasar el ratón por encima.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _titleFont.Dispose();
    }
}

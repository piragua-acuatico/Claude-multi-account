using ClaudeMultiAccount.Core.Setup;

namespace ClaudeMultiAccount.App.ViewModels;

/// <summary>
/// Qué hace desinstalar: quita la herramienta, su entrada y sus accesos directos; conserva por defecto
/// sus datos y nunca toca los de Claude, que se listan para que el usuario decida.
/// </summary>
public sealed class UninstallViewModel(UninstallPlan plan, Func<string, string> display) : ObservableObject
{
    private bool _deleteToolData;

    public IReadOnlyList<string> WillRemove { get; } =
    [
        $"La herramienta: {display(plan.InstallDir)}",
        "Su entrada del menú Inicio y de «Aplicaciones instaladas»",
        plan.ProfileShortcuts.Count == 0
            ? "Ningún acceso directo de perfil (no hay ninguno creado)"
            : $"Los accesos directos de perfil que creó ({plan.ProfileShortcuts.Count})",
    ];

    public string ToolDataText => $"Borrar también la configuración de la herramienta (lista de perfiles, copias de seguridad, registros): {display(plan.ToolDataDir)}";

    /// <summary>Por defecto se conservan: si vuelves a instalar, tus perfiles siguen ahí.</summary>
    public bool DeleteToolData
    {
        get => _deleteToolData;
        set => SetProperty(ref _deleteToolData, value);
    }

    /// <summary>Carpetas de Claude y de los perfiles: no se tocan nunca desde aquí.</summary>
    public IReadOnlyList<string> KeptClaudeFolders { get; } = plan.ClaudeDataFolders.Select(display).ToList();
}

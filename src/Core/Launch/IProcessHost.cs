using ClaudeMultiAccount.Core.Processes;

namespace ClaudeMultiAccount.Core.Launch;

/// <summary>Todo lo que toca procesos del sistema. Interfaz para poder simularlo en pruebas.</summary>
public interface IProcessHost
{
    /// <summary>Crea el proceso y devuelve su raíz (PID + hora de inicio).</summary>
    RootProcess Start(LaunchPlan plan);

    /// <summary>
    /// La raíz y todos sus descendientes, o null si la raíz ya no existe (o su PID es ahora de otro proceso).
    /// Comprobado: todos los procesos de un perfil cuelgan de su Claude.exe raíz.
    /// </summary>
    IReadOnlyList<int>? GetProcessTree(RootProcess root);

    /// <summary>Pide cerrar la ventana principal de cada proceso (cierre amable).</summary>
    void RequestClose(IReadOnlyList<int> processIds);

    /// <summary>Termina los procesos indicados. Solo tras confirmación del usuario.</summary>
    void Terminate(IReadOnlyList<int> processIds);

    /// <summary>
    /// Muestra y trae al frente la ventana principal de esos procesos, también si Claude la tiene escondida en la
    /// bandeja. True si encontró una.
    /// </summary>
    bool TryFocus(IReadOnlyList<int> processIds);

    /// <summary>Procesos principales (raíz) de ese ejecutable de Claude, uno por cada Claude abierto.</summary>
    IReadOnlyList<int> FindRootProcesses(string exePath);

    /// <summary>
    /// Relanza Claude con los datos de un perfil que ya está abierto. Claude no abre otro: le pasa el encargo al
    /// que ya está abierto, que muestra su ventana, y termina en unos segundos. No se registra como raíz del perfil.
    /// Es lo mismo que hace Windows al pulsar Claude en el menú Inicio.
    /// </summary>
    void Activate(LaunchPlan plan);
}

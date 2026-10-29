using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Install;
using ClaudeMultiAccount.Core.Logging;
using ClaudeMultiAccount.Core.Processes;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Launch;

public enum LaunchOutcomeKind
{
    Launched,
    AlreadyRunning,
    Failed,
}

public sealed record LaunchOutcome(LaunchOutcomeKind Kind, string Message, IReadOnlyList<string> Warnings, int? ProcessId = null);

public enum CloseOutcomeKind
{
    Closed,
    NotRunning,
    OpenedOutsideTool,
    StillRunning,
    Terminated,
}

public sealed record CloseOutcome(CloseOutcomeKind Kind, string Message);

/// <summary>Abrir y cerrar perfiles.</summary>
public sealed class LaunchService(
    InstallLocator locator,
    ProcessMonitor monitor,
    RuntimeStateStore runtimeState,
    IProcessHost host,
    IEnvironment env,
    ILogger log)
{
    public LaunchOutcome Launch(Profile profile) => Launch(profile, extraArguments: []);

    /// <summary>
    /// Abre un chat de Code nuevo en el perfil con un enlace <c>claude://code/new</c> (ver CodeChatLink). Si el
    /// perfil está abierto, el enlace se pasa a su Claude (el proceso nuevo se lo entrega al que ya está abierto
    /// y termina); si está cerrado, se abre con él.
    /// </summary>
    public LaunchOutcome OpenCodeChat(Profile profile, string link)
    {
        var located = locator.Locate();
        if (!located.Found)
        {
            return new LaunchOutcome(LaunchOutcomeKind.Failed, located.Error!, []);
        }
        var install = located.Install!;
        var status = monitor.GetStatus(profile, install);
        if (status.State is not (ProfileState.Running or ProfileState.Starting))
        {
            return Launch(profile, [link]);
        }
        if (env.GetDefaultUserEnvironment() is not { } baseEnvironment)
        {
            return new LaunchOutcome(LaunchOutcomeKind.Failed,
                "No se pudo obtener un entorno limpio de Windows; por seguridad no se abrió el chat.", []);
        }
        try
        {
            var plan = LaunchPlanner.Plan(profile, install, baseEnvironment, env);
            host.Activate(plan with { Arguments = [.. plan.Arguments, link] });
            log.Info($"Chat de Code nuevo pedido al perfil '{profile.Name}' ({profile.Id}).");
            return new LaunchOutcome(LaunchOutcomeKind.AlreadyRunning, $"Chat nuevo abierto en '{profile.Name}'.", []);
        }
        catch (Exception ex) when (ex is ProfileNotLaunchableException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.Warn($"No se pudo abrir un chat nuevo en '{profile.Name}' ({profile.Id}): {ex.GetType().Name}.");
            return new LaunchOutcome(LaunchOutcomeKind.Failed, $"No se pudo abrir un chat nuevo en '{profile.Name}': {ex.Message}", []);
        }
    }

    private LaunchOutcome Launch(Profile profile, IReadOnlyList<string> extraArguments)
    {
        // 1. La ruta del exe se resuelve en cada lanzamiento (MSIX la versiona).
        var located = locator.Locate();
        if (!located.Found)
        {
            log.Warn($"Lanzamiento cancelado: {located.Error}");
            return new LaunchOutcome(LaunchOutcomeKind.Failed, located.Error!, []);
        }
        var install = located.Install!;

        // 2. Si ya está abierto, se trae al frente en vez de lanzar otro.
        var status = monitor.GetStatus(profile, install);
        if (status.State is ProfileState.Running or ProfileState.Starting)
        {
            return BringToFront(profile, install, status);
        }

        // 3. Se parte del entorno por defecto de Windows, no del de la consola: si la herramienta se abre desde
        // una sesión de Claude Code, esa consola lleva variables de la otra cuenta (correo, UUID, tokens de sesión).
        var baseEnvironment = env.GetDefaultUserEnvironment();
        if (baseEnvironment is null)
        {
            log.Error($"No se pudo obtener el entorno por defecto de Windows para '{profile.Name}'.");
            return new LaunchOutcome(LaunchOutcomeKind.Failed,
                "No se pudo obtener un entorno limpio de Windows; por seguridad no se abrió Claude.", []);
        }

        // 4. Limpieza de variables que anulan la suscripción y argumentos como lista.
        LaunchPlan plan;
        try
        {
            plan = LaunchPlanner.Plan(profile, install, baseEnvironment, env);
            if (extraArguments.Count > 0)
            {
                plan = plan with { Arguments = [.. plan.Arguments, .. extraArguments] };
            }
        }
        catch (ProfileNotLaunchableException ex)
        {
            return new LaunchOutcome(LaunchOutcomeKind.Failed, ex.Message, []);
        }

        var warnings = new List<string>();
        if (plan.RemovedOverrides.Count > 0)
        {
            warnings.Add("Se quitaron del entorno de Claude estas variables (seguían en tu sistema): " +
                         string.Join(", ", plan.RemovedOverrides) + ".");
        }

        // 5. Se crea el proceso y se recuerda su raíz para encontrar su árbol en ejecuciones posteriores.
        try
        {
            var root = host.Start(plan);
            try
            {
                runtimeState.Set(profile.Id, root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add("Claude se abrió, pero no se pudo guardar su estado: la herramienta no podrá cerrarlo.");
            }
            log.Info($"Perfil '{profile.Name}' ({profile.Id}) lanzado; pid {root.Pid}; Claude {install.Version}.");
            foreach (var warning in warnings)
            {
                log.Warn(warning);
            }
            return new LaunchOutcome(LaunchOutcomeKind.Launched,
                $"'{profile.Name}' abierto (Claude {install.Version}).", warnings, root.Pid);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.Error($"No se pudo lanzar el perfil '{profile.Name}' ({profile.Id}).", ex);
            return new LaunchOutcome(LaunchOutcomeKind.Failed, $"No se pudo abrir '{profile.Name}': {ex.Message}", warnings);
        }
    }

    /// <summary>
    /// Trae al frente un perfil ya abierto, aunque Claude esté escondido en la bandeja: se muestra su ventana
    /// directamente. Con un perfil abierto fuera de la herramienta se busca el único Claude abierto que no es de
    /// ningún perfil de la herramienta. Solo si no aparece ninguna ventana se relanza Claude con sus datos, porque
    /// Claude 3 lo toma como «abrir una sesión» y avisa «That session is no longer available» (2026-10-26).
    /// </summary>
    private LaunchOutcome BringToFront(Profile profile, ClaudeInstall install, ProfileStatus status)
    {
        var focused = host.TryFocus(status.ProcessIds);
        if (!focused && status.OpenedOutsideTool)
        {
            var ours = runtimeState.All().SelectMany(root => host.GetProcessTree(root) ?? []).ToHashSet();
            var outside = host.FindRootProcesses(install.ExePath).Where(pid => !ours.Contains(pid)).ToList();
            if (outside.Count == 1)
            {
                focused = host.TryFocus(outside);
            }
        }
        if (focused)
        {
            return new LaunchOutcome(LaunchOutcomeKind.AlreadyRunning, $"'{profile.Name}' traído al frente.", []);
        }

        var activated = false;
        if (env.GetDefaultUserEnvironment() is { } baseEnvironment)
        {
            try
            {
                host.Activate(LaunchPlanner.Plan(profile, install, baseEnvironment, env));
                activated = true;
            }
            catch (Exception ex) when (ex is ProfileNotLaunchableException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                log.Warn($"No se pudo pedir a '{profile.Name}' ({profile.Id}) que muestre su ventana: {ex.GetType().Name}.");
            }
        }

        var message = activated ? $"Mostrando '{profile.Name}'… (puede tardar unos segundos)."
            : $"'{profile.Name}' ya está abierto, pero no se pudo traer al frente: búscalo en la barra de tareas o junto al reloj.";
        return new LaunchOutcome(LaunchOutcomeKind.AlreadyRunning, message, []);
    }

    /// <summary>
    /// Cierre amable; si sigue abierto tras <paramref name="timeout"/>, solo se fuerza con <paramref name="force"/>
    /// (puede perder trabajo no guardado de sesiones de Code, por eso lo decide el usuario).
    /// </summary>
    public CloseOutcome Close(Profile profile, bool force, TimeSpan timeout)
    {
        var install = locator.Locate().Install;
        var status = monitor.GetStatus(profile, install);

        if (status.ProcessIds.Count == 0)
        {
            return status.State switch
            {
                ProfileState.Running => new CloseOutcome(CloseOutcomeKind.OpenedOutsideTool,
                    $"'{profile.Name}' se abrió fuera de esta herramienta; ciérralo desde su ventana."),
                ProfileState.Error => new CloseOutcome(CloseOutcomeKind.OpenedOutsideTool,
                    $"No se pudo comprobar si '{profile.Name}' está abierto; si lo está, ciérralo desde su ventana."),
                _ => new CloseOutcome(CloseOutcomeKind.NotRunning, $"'{profile.Name}' no está abierto."),
            };
        }

        host.RequestClose(status.ProcessIds);
        if (WaitUntilClosed(profile, timeout))
        {
            runtimeState.Remove(profile.Id);
            log.Info($"Perfil '{profile.Name}' ({profile.Id}) cerrado.");
            return new CloseOutcome(CloseOutcomeKind.Closed, $"'{profile.Name}' cerrado.");
        }

        if (!force)
        {
            return new CloseOutcome(CloseOutcomeKind.StillRunning,
                $"'{profile.Name}' sigue abierto (puede que Claude se haya quedado en la bandeja). " +
                "Para forzar el cierre usa --forzar; se perderá lo que no esté guardado.");
        }

        // Se vuelve a leer el árbol justo antes: solo se termina lo que sigue colgando de la raíz del perfil.
        host.Terminate(monitor.GetProcessIds(profile));
        if (!WaitUntilClosed(profile, TimeSpan.FromSeconds(5)))
        {
            log.Error($"Perfil '{profile.Name}' ({profile.Id}): no se pudo cerrar ni a la fuerza.");
            return new CloseOutcome(CloseOutcomeKind.StillRunning, $"No se pudo cerrar '{profile.Name}' ni a la fuerza.");
        }
        runtimeState.Remove(profile.Id);
        log.Warn($"Perfil '{profile.Name}' ({profile.Id}) cerrado a la fuerza por petición del usuario.");
        return new CloseOutcome(CloseOutcomeKind.Terminated, $"'{profile.Name}' cerrado a la fuerza.");
    }

    private bool WaitUntilClosed(Profile profile, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (monitor.GetProcessIds(profile).Count == 0)
            {
                return true;
            }
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }
            Thread.Sleep(250);
        }
    }
}

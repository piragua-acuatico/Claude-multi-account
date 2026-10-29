using System.Globalization;
using System.Text;
using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Messaging;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Cli;

/// <summary>
/// <c>cma mensaje …</c>: lo que usan la skill y el hook de cada perfil para pasarse mensajes. No escribe registros
/// ni nada en %LOCALAPPDATA% (lo lanza Claude Code desde dentro del paquete MSIX de Claude; ver AppPaths.MailboxDir).
/// </summary>
internal sealed class MessageCommands(ProfileStore store, Mailbox mailbox, IEnvironment env)
{
    private const int Ok = 0;
    private const int Failure = 1;
    private const int Usage = 2;

    private static readonly string[] KnownOptions = ["--de", "--para", "--archivo", "--texto", "--cuantos", "--carpeta"];

    /// <summary>Opciones sin valor.</summary>
    private const string NoChatFlag = "--sin-chat";

    public static string HelpText => """
          cma mensaje perfiles --de <perfil>              Perfiles a los que se puede escribir.
          cma mensaje enviar --de <perfil> --para <perfil> (--archivo <ruta> | --texto <texto>)
                             [--carpeta <carpeta>] [--sin-chat]
                                                          Deja un mensaje a otro perfil. Claude Multi-Account le abre
                                                          un chat de Code nuevo con él (en --carpeta o en la actual);
                                                          con --sin-chat llega al chat que ya tenga abierto.
          cma mensaje leer --de <perfil>                  Muestra los mensajes nuevos y los marca como leídos.
          cma mensaje historial --de <perfil> [--cuantos N]  Últimos mensajes enviados y recibidos.
                                                          Sin --de, se usa el perfil del Claude que lo ejecuta.
        """;

    public int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Falta la acción: perfiles, enviar, leer o historial.");
            Console.Error.WriteLine(HelpText);
            return Usage;
        }
        var action = args[0].ToLowerInvariant();
        if (action == "hook")
        {
            return Hook(args.Skip(1).ToArray());
        }
        if (!TryParse(args.Skip(1).ToArray(), out var options, out var error))
        {
            Console.Error.WriteLine(error);
            return Usage;
        }
        return action switch
        {
            "perfiles" => Profiles(options),
            "enviar" => Send(options),
            "leer" => Read(options),
            "historial" => History(options),
            _ => UnknownAction(action),
        };
    }

    /// <summary>
    /// Hook UserPromptSubmit de Claude Code: lo que escribe en la salida se añade al chat. Nunca falla ni bloquea
    /// el mensaje del usuario: ante cualquier problema no dice nada.
    /// </summary>
    private int Hook(string[] args)
    {
        try
        {
            if (Console.IsInputRedirected)
            {
                // Claude Code manda el contexto del turno por la entrada; se lee entero para no dejarle esperando.
                _ = Console.In.ReadToEnd();
            }
            if (!TryParse(args, out var options, out _) || FindSelf(options, quiet: true) is not { } self)
            {
                return Ok;
            }
            var delivered = mailbox.TakeNew(self.Id);
            var text = Mailbox.FormatForChat(delivered, mailbox.PeekNew(self.Id).Count);
            if (text.Length > 0)
            {
                Console.WriteLine(text);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Un fallo del buzón no puede impedir que el usuario use Claude.
        }
        return Ok;
    }

    private int Profiles(Dictionary<string, string> options)
    {
        if (FindSelf(options) is not { } self)
        {
            return Failure;
        }
        var others = store.Load().Profiles
            .Where(p => p.Id != self.Id && MessagingSetup.IsMessagingProfile(p))
            .ToList();
        Console.WriteLine($"Estás en «{self.Name}».");
        if (others.Count == 0)
        {
            Console.WriteLine("No hay otros perfiles a los que escribir.");
            return Ok;
        }
        Console.WriteLine("Puedes escribir a:");
        foreach (var profile in others)
        {
            Console.WriteLine($"  - {profile.Name}");
        }
        return Ok;
    }

    private int Send(Dictionary<string, string> options)
    {
        if (FindSelf(options) is not { } self)
        {
            return Failure;
        }
        if (!options.TryGetValue("--para", out var toName))
        {
            Console.Error.WriteLine("Indica el destinatario con --para \"<nombre del perfil>\".");
            return Usage;
        }
        if (store.Load().Find(toName) is not { } to)
        {
            Console.Error.WriteLine($"No existe el perfil '{toName}'. Usa 'cma mensaje perfiles' para verlos.");
            return Failure;
        }
        string text;
        if (options.TryGetValue("--archivo", out var file))
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"No existe el archivo '{file}'.");
                return Failure;
            }
            text = File.ReadAllText(file, Encoding.UTF8);
        }
        else if (options.TryGetValue("--texto", out var inline))
        {
            text = inline;
        }
        else
        {
            Console.Error.WriteLine("Indica el mensaje con --archivo <ruta> o --texto \"…\".");
            return Usage;
        }

        var openChat = !options.ContainsKey(NoChatFlag);
        var folder = options.TryGetValue("--carpeta", out var chosen) ? chosen : Environment.CurrentDirectory;
        try
        {
            mailbox.Send(self, to, text, DateTime.UtcNow, openChat, Path.IsPathFullyQualified(folder) ? Path.GetFullPath(folder) : null);
        }
        catch (MailboxException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return Failure;
        }
        Console.WriteLine(openChat
            ? $"Mensaje enviado a «{to.Name}». Claude Multi-Account le abre un chat de Code nuevo con el mensaje escrito " +
              $"para que el usuario lo envíe (si la herramienta no está abierta, le llegará cuando el usuario escriba en un chat de «{to.Name}»)."
            : $"Mensaje enviado a «{to.Name}». Le llegará al chat que ya tiene abierto cuando el usuario le escriba.");
        return Ok;
    }

    private int Read(Dictionary<string, string> options)
    {
        if (FindSelf(options) is not { } self)
        {
            return Failure;
        }
        var delivered = mailbox.TakeNew(self.Id);
        if (delivered.Count == 0)
        {
            Console.WriteLine("No hay mensajes nuevos.");
            return Ok;
        }
        Console.WriteLine(Mailbox.FormatForChat(delivered, mailbox.PeekNew(self.Id).Count));
        return Ok;
    }

    private int History(Dictionary<string, string> options)
    {
        if (FindSelf(options) is not { } self)
        {
            return Failure;
        }
        var count = 10;
        if (options.TryGetValue("--cuantos", out var raw) && (!int.TryParse(raw, out count) || count < 1))
        {
            Console.Error.WriteLine("--cuantos debe ser un número mayor que 0.");
            return Usage;
        }
        var entries = mailbox.History(self.Id, count);
        if (entries.Count == 0)
        {
            Console.WriteLine("Todavía no hay mensajes.");
            return Ok;
        }
        foreach (var entry in entries)
        {
            var message = entry.Message;
            var when = message.SentUtc.ToLocalTime().ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);
            Console.WriteLine(entry.Sent
                ? $"→ Para «{message.ToName}», {when}:"
                : $"← De «{message.FromName}», {when}{(entry.Unread ? " (sin leer)" : "")}:");
            Console.WriteLine(message.Text);
            Console.WriteLine();
        }
        return Ok;
    }

    /// <summary>El perfil que escribe: el de --de o, si no se indica, el del Claude que ejecuta la orden.</summary>
    private Profile? FindSelf(Dictionary<string, string> options, bool quiet = false)
    {
        var document = store.Load();
        Profile? self;
        if (options.TryGetValue("--de", out var nameOrId))
        {
            self = document.Find(nameOrId);
        }
        else
        {
            // Los perfiles aislados se abren con CLAUDE_CONFIG_DIR; el principal, sin ella.
            var configDir = env.Get("CLAUDE_CONFIG_DIR");
            var setup = new MessagingSetup(env);
            self = string.IsNullOrWhiteSpace(configDir)
                ? document.Profiles.FirstOrDefault(p => p.Type == ProfileType.Main)
                : document.Profiles.FirstOrDefault(p =>
                    setup.ConfigDirOf(p) is { } dir && PathUtil.AreSame(dir, PathUtil.Expand(configDir, env)));
        }
        if (self is null && !quiet)
        {
            Console.Error.WriteLine("No se sabe desde qué perfil escribes: indícalo con --de \"<nombre del perfil>\".");
        }
        return self;
    }

    private static int UnknownAction(string action)
    {
        Console.Error.WriteLine($"Acción desconocida: {action}. Usa perfiles, enviar, leer o historial.");
        Console.Error.WriteLine(HelpText);
        return Usage;
    }

    /// <summary>Opciones con valor (<c>--para "Claude Pro"</c>). Los valores pueden empezar por / (rutas).</summary>
    private static bool TryParse(string[] args, out Dictionary<string, string> options, out string error)
    {
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = "";
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i].ToLowerInvariant();
            if (name == NoChatFlag)
            {
                options[name] = "";
                continue;
            }
            if (!KnownOptions.Contains(name))
            {
                error = $"Opción desconocida para 'mensaje': {args[i]}";
                return false;
            }
            if (i + 1 >= args.Length)
            {
                error = $"Falta el valor de {args[i]}.";
                return false;
            }
            options[name] = args[++i];
        }
        return true;
    }
}

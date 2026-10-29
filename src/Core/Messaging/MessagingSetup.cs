using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Paths;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.Core.Messaging;

/// <summary>
/// Activa los mensajes entre cuentas en cada perfil, dentro de su configuración de Claude Code:
/// un hook <c>UserPromptSubmit</c> en <c>settings.json</c> que entrega los mensajes nuevos al escribir en un chat,
/// y la skill <c>mensajes-entre-cuentas</c> para enviarlos. Solo añade y quita lo suyo: el resto de
/// <c>settings.json</c> se conserva, y si el archivo no se puede leer no se toca.
/// </summary>
public sealed class MessagingSetup(IEnvironment env)
{
    public const string SkillName = "mensajes-entre-cuentas";

    /// <summary>Lo que distingue el hook de la herramienta de los del usuario.</summary>
    private const string HookMarker = " mensaje hook --de ";

    private const string HookEvent = "UserPromptSubmit";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Carpeta de configuración de Claude Code del perfil (la de siempre en el principal).</summary>
    public string? ConfigDirOf(Profile profile)
    {
        var raw = profile.Type == ProfileType.Main ? KnownPaths.MainClaudeConfigDir : profile.ClaudeConfigDir;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var expanded = PathUtil.Expand(raw, env);
        return PathUtil.IsUsableAbsolute(expanded) ? expanded : null;
    }

    /// <summary>
    /// Pone (o actualiza) el hook y la skill en los perfiles de Claude Desktop. Solo escribe lo que cambia.
    /// Devuelve un problema por perfil que no se pudo preparar.
    /// </summary>
    public IReadOnlyList<string> Install(IEnumerable<Profile> profiles, string cmaPath)
    {
        var problems = new List<string>();
        foreach (var profile in profiles.Where(IsMessagingProfile))
        {
            if (ConfigDirOf(profile) is not { } configDir)
            {
                problems.Add($"«{profile.Name}»: no tiene carpeta de configuración de Claude Code.");
                continue;
            }
            try
            {
                var settingsPath = Path.Combine(configDir, "settings.json");
                if (EditSettings(settingsPath, root => SetHook(root, HookCommand(cmaPath, profile.Id))) is { } error)
                {
                    problems.Add($"«{profile.Name}»: {error}");
                    continue;
                }
                WriteIfChanged(SkillFile(configDir), SkillText(profile, cmaPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"«{profile.Name}»: {ex.Message}");
            }
        }
        return problems;
    }

    /// <summary>Quita el hook y la skill de la herramienta; lo demás de cada perfil queda como estaba.</summary>
    public IReadOnlyList<string> Remove(IEnumerable<Profile> profiles)
    {
        var problems = new List<string>();
        foreach (var profile in profiles)
        {
            if (ConfigDirOf(profile) is not { } configDir)
            {
                continue;
            }
            try
            {
                var settingsPath = Path.Combine(configDir, "settings.json");
                if (File.Exists(settingsPath) && EditSettings(settingsPath, RemoveHook) is { } error)
                {
                    problems.Add($"«{profile.Name}»: {error}");
                }
                var skillFile = SkillFile(configDir);
                if (File.Exists(skillFile))
                {
                    File.Delete(skillFile);
                    var skillDir = Path.GetDirectoryName(skillFile)!;
                    if (Directory.GetFileSystemEntries(skillDir).Length == 0)
                    {
                        Directory.Delete(skillDir);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"«{profile.Name}»: {ex.Message}");
            }
        }
        return problems;
    }

    /// <summary>Si el perfil ya tiene el hook de la herramienta.</summary>
    public bool IsInstalled(Profile profile)
    {
        if (ConfigDirOf(profile) is not { } configDir)
        {
            return false;
        }
        var settingsPath = Path.Combine(configDir, "settings.json");
        try
        {
            return File.Exists(settingsPath)
                && JsonNode.Parse(File.ReadAllText(settingsPath), documentOptions: ReadOptions) is JsonObject root
                && OurGroups(root).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static bool IsMessagingProfile(Profile profile) =>
        profile.Type is ProfileType.Main or ProfileType.DesktopIsolated;

    /// <summary>
    /// Orden del hook. Ruta con barras normales y entre comillas: funciona igual en Git Bash y en cmd,
    /// y con espacios en la ruta.
    /// </summary>
    public static string HookCommand(string cmaPath, Guid profileId) =>
        $"\"{cmaPath.Replace('\\', '/')}\"{HookMarker}{profileId:D}";

    private static string SkillFile(string configDir) => Path.Combine(configDir, "skills", SkillName, "SKILL.md");

    /// <summary>Lee, cambia y guarda settings.json. Devuelve el problema si no se pudo (el archivo no se toca).</summary>
    private static string? EditSettings(string path, Action<JsonObject> change)
    {
        JsonObject root;
        string? original = null;
        if (File.Exists(path))
        {
            original = File.ReadAllText(path);
            JsonNode? parsed;
            try
            {
                parsed = string.IsNullOrWhiteSpace(original) ? new JsonObject() : JsonNode.Parse(original, documentOptions: ReadOptions);
            }
            catch (JsonException)
            {
                return "su settings.json no es JSON válido; no se tocó.";
            }
            if (parsed is not JsonObject obj)
            {
                return "su settings.json no tiene el formato esperado; no se tocó.";
            }
            root = obj;
        }
        else
        {
            root = new JsonObject();
        }

        var before = root.ToJsonString();
        change(root);
        if (original is not null && root.ToJsonString() == before)
        {
            return null;
        }
        WriteIfChanged(path, root.ToJsonString(WriteOptions) + Environment.NewLine);
        return null;
    }

    private static void SetHook(JsonObject root, string command)
    {
        var groups = HookGroups(root, create: true)!;
        var ours = OurGroups(root).ToList();
        if (ours.Count == 1 && Commands(ours[0]).SequenceEqual([command]))
        {
            return;
        }
        foreach (var group in ours)
        {
            groups.Remove(group);
        }
        groups.Add(new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command }),
        });
    }

    private static void RemoveHook(JsonObject root)
    {
        if (HookGroups(root, create: false) is not { } groups)
        {
            return;
        }
        foreach (var group in OurGroups(root).ToList())
        {
            groups.Remove(group);
        }
        // Si solo estaba lo de la herramienta, se deja settings.json como antes.
        if (groups.Count == 0 && root["hooks"] is JsonObject hooks)
        {
            hooks.Remove(HookEvent);
            if (hooks.Count == 0)
            {
                root.Remove("hooks");
            }
        }
    }

    private static JsonArray? HookGroups(JsonObject root, bool create)
    {
        if (root["hooks"] is not JsonObject hooks)
        {
            if (!create)
            {
                return null;
            }
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }
        if (hooks[HookEvent] is not JsonArray groups)
        {
            if (!create)
            {
                return null;
            }
            groups = [];
            hooks[HookEvent] = groups;
        }
        return groups;
    }

    private static IEnumerable<JsonObject> OurGroups(JsonObject root) =>
        HookGroups(root, create: false)?.OfType<JsonObject>()
            .Where(g => Commands(g).Any(c => c.Contains(HookMarker, StringComparison.Ordinal)))
        ?? [];

    private static IEnumerable<string> Commands(JsonObject group) =>
        group["hooks"] is JsonArray hooks
            ? hooks.OfType<JsonObject>().Select(h => h["command"]?.GetValueKind() == JsonValueKind.String ? h["command"]!.GetValue<string>() : "")
            : [];

    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content)
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".cma.tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Instrucciones para el Claude de cada perfil. Llevan su id para no depender de variables de entorno.</summary>
    private static string SkillText(Profile profile, string cmaPath)
    {
        var cma = $"\"{cmaPath.Replace('\\', '/')}\"";
        var id = profile.Id.ToString("D");
        return $$"""
            ---
            name: {{SkillName}}
            description: Enviar y leer mensajes entre las cuentas de Claude de este PC (perfiles de Claude Multi-Account). Úsala cuando el usuario pida pasar, contar, enviar o preguntar algo a otra de sus cuentas (por ejemplo «dile a Pro…», «pásale esto a Max», «¿tengo mensajes?») o responder a un mensaje recibido.
            ---

            # Mensajes entre cuentas

            Estás en el perfil «{{profile.Name}}». El usuario tiene otras cuentas de Claude en este PC y puede pedirte
            que les pases información. Los mensajes se guardan solo en este PC; la cuenta que los recibe los ve cuando
            el usuario escribe en uno de sus chats de Code.

            ## Reglas

            - Envía mensajes solo cuando el usuario lo pida, nunca por iniciativa propia.
            - Sirven para pasar información. No encargues trabajo a otra cuenta para repartir el uso entre cuentas ni
              respondas en bucle: eso va contra los términos de Anthropic.
            - Nunca incluyas contraseñas, claves, tokens ni otros secretos (la herramienta rechaza el mensaje si los detecta).
            - Para pasar archivos, escribe su ruta completa: todas las cuentas ven el mismo disco.

            ## A quién se puede escribir

            ```
            {{cma}} mensaje perfiles --de {{id}}
            ```

            ## Enviar

            1. Escribe el mensaje en un archivo de texto UTF-8 en la carpeta temporal. Que se entienda sin ver esta
               conversación: de qué va, carpeta o repositorio, qué se hizo, qué falta y lo que el usuario quiera pasar.
            2. Elige la carpeta del chat nuevo de la otra cuenta. Siempre una sola carpeta:
               - **Es sobre lo mismo en lo que trabajáis aquí:** la carpeta del proyecto actual. Si las dos cuentas
                 van a cambiar archivos a la vez, díselo al usuario: pueden pisarse los cambios.
               - **Es un trabajo nuevo y separado:** crea una carpeta nueva, vacía y con un nombre corto y claro
                 (sin espacios raros), junto a la del proyecto actual (en su carpeta padre), y usa esa.
               - **Si no tienes claro cuál es o dónde crearla:** no envíes todavía. Pregúntale al usuario qué carpeta
                 usar o si prefiere crearla él, y espera su respuesta.
            3. Para empezar una conversación con otra cuenta, ejecuta:
               ```
               {{cma}} mensaje enviar --de {{id}} --para "<nombre del perfil>" --archivo "<ruta del archivo>" --carpeta "<carpeta elegida>"
               ```
               Claude Multi-Account abre en esa cuenta un chat de Code nuevo, en esa carpeta, con el mensaje
               escrito; el usuario lo envía cuando quiera.
            4. Si es una respuesta a un mensaje que recibiste, añade `--sin-chat`: le llega al chat que la otra cuenta
               ya tiene abierto, cuando el usuario le escriba.
            5. Dile al usuario a quién se lo enviaste, en qué carpeta se abrirá y un resumen de una línea.

            ## Leer

            Los mensajes nuevos te llegan solos cuando el usuario escribe. Para revisarlos a mano o ver los anteriores:

            ```
            {{cma}} mensaje leer --de {{id}}
            {{cma}} mensaje historial --de {{id}}
            ```

            """;
    }
}

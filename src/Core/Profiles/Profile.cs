using System.Text.Json.Serialization;

namespace ClaudeMultiAccount.Core.Profiles;

public enum ProfileType
{
    /// <summary>La instalación y los datos actuales de Claude, sin tocar.</summary>
    Main,

    /// <summary>Claude Desktop oficial con --user-data-dir y CLAUDE_CONFIG_DIR propios.</summary>
    DesktopIsolated,

    /// <summary>Claude Code en terminal con CLAUDE_CONFIG_DIR propio (todavía no se puede abrir).</summary>
    Cli,
}

/// <summary>Perfil guardado en profiles.json. No contiene secretos: no existe ningún campo para ellos.</summary>
public sealed class Profile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public ProfileType Type { get; set; }
    public string IconColor { get; set; } = "#D97757";
    public string IconGlyph { get; set; } = "";

    /// <summary>Valor de --user-data-dir. Puede contener %VARIABLES%. Vacío en el perfil principal.</summary>
    public string? DesktopDataDir { get; set; }

    /// <summary>Valor de CLAUDE_CONFIG_DIR. Puede contener %VARIABLES%. Vacío en el perfil principal.</summary>
    public string? ClaudeConfigDir { get; set; }

    public List<string> WorkFolders { get; set; } = [];

    /// <summary>Texto libre que escribe el usuario para recordar la cuenta. Nunca se lee de Claude.</summary>
    public string EmailHint { get; set; } = "";

    public DateTime? LastLaunchedUtc { get; set; }

    /// <summary>
    /// Cuándo confirmó el usuario, en el asistente, que este perfil tiene la cuenta correcta. Es lo que
    /// dice el usuario, no algo que la herramienta lea de Claude (nunca lee sesiones ni credenciales).
    /// </summary>
    public DateTime? LoginConfirmedUtc { get; set; }

    /// <summary>
    /// Perfil aislado al que aún no se le ha hecho el primer inicio de sesión guiado. Los que ya se abrieron
    /// antes de existir el asistente (como los de las primeras versiones) se dan por iniciados.
    /// </summary>
    [JsonIgnore]
    public bool NeedsLoginWizard => Type == ProfileType.DesktopIsolated && LoginConfirmedUtc is null && LastLaunchedUtc is null;

    /// <summary>Accesos directos (.lnk) que la herramienta creó para este perfil, para renombrarlos o quitarlos con él.</summary>
    public List<string> Shortcuts { get; set; } = [];
    public string Notes { get; set; } = "";

    /// <summary>Campos que esta versión no conoce (de una versión más nueva): se conservan al guardar.</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

public sealed class ProfileDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<Profile> Profiles { get; set; } = [];

    /// <summary>Si el usuario activó los mensajes entre cuentas (hook y skill en cada perfil). Apagado no se escribe.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MessagingEnabled { get; set; }

    /// <summary>Campos que esta versión no conoce: se conservan al guardar.</summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }

    /// <summary>Busca por id o por nombre (sin distinguir mayúsculas).</summary>
    public Profile? Find(string nameOrId)
    {
        if (Guid.TryParse(nameOrId, out var id))
        {
            var byId = Profiles.FirstOrDefault(p => p.Id == id);
            if (byId is not null)
            {
                return byId;
            }
        }
        return Profiles.FirstOrDefault(p => string.Equals(p.Name, nameOrId.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

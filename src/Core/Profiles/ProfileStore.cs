using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ClaudeMultiAccount.Core.Abstractions;
using ClaudeMultiAccount.Core.Paths;

namespace ClaudeMultiAccount.Core.Profiles;

public sealed class ProfileStoreException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Lee y guarda profiles.json. Guardar valida antes y escribe de forma atómica.</summary>
public sealed class ProfileStore(AppPaths paths, IFileSystem fs, IEnvironment env)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Acentos legibles en el archivo; se siguen escapando los caracteres sensibles en HTML.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) },
    };

    public string FilePath => paths.ProfilesFile;

    public bool Exists => fs.FileExists(paths.ProfilesFile);

    /// <summary>Si el archivo no existe devuelve un documento vacío (sin crear nada en disco).</summary>
    public ProfileDocument Load()
    {
        if (!fs.FileExists(paths.ProfilesFile))
        {
            return new ProfileDocument();
        }

        try
        {
            var document = JsonSerializer.Deserialize<ProfileDocument>(fs.ReadAllText(paths.ProfilesFile), JsonOptions)
                ?? throw new ProfileStoreException("profiles.json está vacío.");
            if (document.SchemaVersion > ProfileDocument.CurrentSchemaVersion)
            {
                throw new ProfileStoreException(
                    $"profiles.json usa la versión de esquema {document.SchemaVersion}, más nueva que la que entiende esta herramienta.");
            }
            return FillMissing(document);
        }
        catch (JsonException ex)
        {
            // No se repite el contenido del archivo en el mensaje.
            throw new ProfileStoreException($"profiles.json no es JSON válido (línea {ex.LineNumber + 1}).", ex);
        }
    }

    /// <summary>
    /// Un profiles.json editado a mano puede traer «null» donde se espera texto o una lista: se cambia por el valor
    /// por defecto, para que la validación dé un mensaje claro en vez de fallar el programa.
    /// </summary>
    private static ProfileDocument FillMissing(ProfileDocument document)
    {
        document.Profiles = (document.Profiles ?? []).Where(p => p is not null).ToList();
        foreach (var profile in document.Profiles)
        {
            profile.Name ??= "";
            profile.IconColor ??= "";
            profile.IconGlyph ??= "";
            profile.EmailHint ??= "";
            profile.Notes ??= "";
            profile.WorkFolders = (profile.WorkFolders ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            profile.Shortcuts = (profile.Shortcuts ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }
        return document;
    }

    public void Save(ProfileDocument document)
    {
        var errors = ProfileValidator.Validate(document, env);
        if (errors.Count > 0)
        {
            throw new ProfileStoreException("No se guardó profiles.json:" + Environment.NewLine + " - " +
                                            string.Join(Environment.NewLine + " - ", errors));
        }
        fs.WriteAllTextAtomic(paths.ProfilesFile, JsonSerializer.Serialize(document, JsonOptions));
    }
}

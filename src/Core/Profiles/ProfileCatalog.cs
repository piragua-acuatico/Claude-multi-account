namespace ClaudeMultiAccount.Core.Profiles;

/// <summary>Lo que el usuario puede cambiar de un perfil desde el editor.</summary>
public sealed record ProfileEdit(
    string Name,
    string IconColor,
    string IconGlyph,
    string EmailHint,
    IReadOnlyList<string> WorkFolders);

/// <summary>
/// Crear, editar y quitar perfiles en profiles.json. Cada operación relee el archivo, aplica el cambio y lo
/// guarda validado y de forma atómica (ProfileStore). Nunca crea, mueve ni borra carpetas de datos:
/// Claude crea las suyas al abrir el perfil por primera vez, y al quitar un perfil sus datos se conservan
/// (SECURITY.md T10).
/// </summary>
public sealed class ProfileCatalog(ProfileStore store)
{
    /// <summary>Lista inicial: solo el perfil principal, que es el Claude Desktop de siempre con sus datos de siempre.</summary>
    public static ProfileDocument CreateInitialDocument()
    {
        var document = new ProfileDocument();
        document.Profiles.Add(new Profile
        {
            Id = Guid.NewGuid(),
            Name = "Claude principal",
            Type = ProfileType.Main,
            IconColor = "#D97757",
            IconGlyph = "C",
            Notes = "Tu Claude Desktop de siempre, con sus datos de siempre.",
        });
        return document;
    }

    /// <summary>Primer arranque: si no existe profiles.json, lo crea con el perfil principal. Devuelve si lo creó.</summary>
    public bool CreateIfMissing()
    {
        if (store.Exists)
        {
            return false;
        }
        store.Save(CreateInitialDocument());
        return true;
    }

    /// <summary>
    /// Apunta cuándo se abrió el perfil. Relee el archivo y solo cambia esa fecha, para no pisar lo que otro
    /// proceso (la ventana o la CLI) haya guardado mientras tanto.
    /// </summary>
    public void RecordLaunch(Guid id, DateTime launchedUtc)
    {
        var document = store.Load();
        var profile = document.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }
        profile.LastLaunchedUtc = launchedUtc;
        store.Save(document);
    }

    /// <param name="isTaken">Si una carpeta (ruta sin expandir) ya existe en disco; ver <see cref="ProfileFolderNames.Suggest"/>.</param>
    public ProfileFolders PreviewFolders(string name, Func<string, bool> isTaken) =>
        PreviewFolders(store.Load(), name, isTaken);

    /// <summary>Nuevo perfil "Desktop aislado" con carpetas propias. Devuelve el perfil guardado.</summary>
    public Profile CreateIsolated(ProfileEdit edit, Func<string, bool> isTaken)
    {
        var document = store.Load();
        var folders = PreviewFolders(document, edit.Name, isTaken);
        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            Type = ProfileType.DesktopIsolated,
            DesktopDataDir = folders.DesktopDataDir,
            ClaudeConfigDir = folders.ClaudeConfigDir,
        };
        Apply(profile, edit);
        document.Profiles.Add(profile);
        store.Save(document);
        return profile;
    }

    /// <summary>Cambia nombre, color, inicial, correo de referencia y carpetas de trabajo. El tipo y las carpetas de datos no cambian.</summary>
    public void Update(Guid id, ProfileEdit edit)
    {
        var document = store.Load();
        var profile = document.Profiles.FirstOrDefault(p => p.Id == id)
            ?? throw new ProfileStoreException("Ese perfil ya no está en profiles.json.");
        Apply(profile, edit);
        store.Save(document);
    }

    /// <summary>Fin del asistente de inicio de sesión: el usuario confirmó la cuenta (y quizá escribió su correo).</summary>
    public void ConfirmLogin(Guid id, string emailHint, DateTime confirmedUtc)
    {
        var document = store.Load();
        var profile = document.Profiles.FirstOrDefault(p => p.Id == id)
            ?? throw new ProfileStoreException("Ese perfil ya no está en profiles.json.");
        profile.LoginConfirmedUtc = confirmedUtc;
        profile.LastLaunchedUtc ??= confirmedUtc;
        if (!string.IsNullOrWhiteSpace(emailHint))
        {
            profile.EmailHint = emailHint.Trim();
        }
        store.Save(document);
    }

    /// <summary>Recuerda los accesos directos creados para el perfil.</summary>
    public void SetShortcuts(Guid id, IReadOnlyList<string> shortcuts)
    {
        var document = store.Load();
        var profile = document.Profiles.FirstOrDefault(p => p.Id == id)
            ?? throw new ProfileStoreException("Ese perfil ya no está en profiles.json.");
        profile.Shortcuts = shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        store.Save(document);
    }

    /// <summary>Quita el perfil de la lista. Sus carpetas y su sesión se quedan en disco.</summary>
    public void Remove(Guid id)
    {
        var document = store.Load();
        var profile = document.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }
        if (profile.Type == ProfileType.Main)
        {
            throw new ProfileStoreException("El perfil principal no se puede quitar.");
        }
        document.Profiles.Remove(profile);
        store.Save(document);
    }

    private static ProfileFolders PreviewFolders(ProfileDocument document, string name, Func<string, bool> isTaken)
    {
        var used = document.Profiles
            .SelectMany(p => new[] { p.DesktopDataDir, p.ClaudeConfigDir })
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ProfileFolderNames.Suggest(name, dir => used.Contains(dir) || isTaken(dir));
    }

    private static void Apply(Profile profile, ProfileEdit edit)
    {
        profile.Name = edit.Name.Trim();
        profile.IconColor = edit.IconColor;
        profile.IconGlyph = edit.IconGlyph.Trim();
        profile.EmailHint = edit.EmailHint.Trim();
        profile.WorkFolders = edit.WorkFolders
            .Select(TrimFolder)
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Sin espacios ni barra final (con o sin ella es la misma carpeta), salvo en la raíz de una unidad.</summary>
    private static string TrimFolder(string folder)
    {
        var trimmed = folder.Trim();
        var withoutSeparator = trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return withoutSeparator.EndsWith(':') ? withoutSeparator + Path.DirectorySeparatorChar : withoutSeparator;
    }
}

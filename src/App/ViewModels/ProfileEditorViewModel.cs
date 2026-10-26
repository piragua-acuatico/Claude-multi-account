using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.App.ViewModels;

public sealed record ColorOption(string Hex, string Label);

/// <summary>
/// Formulario de "Nuevo perfil" / "Editar perfil".
/// Los perfiles nuevos son siempre "Desktop aislado"; sus carpetas de datos se calculan del nombre y no cambian
/// después. Del perfil principal no se tocan el tipo ni las carpetas de datos.
/// </summary>
public sealed class ProfileEditorViewModel : ObservableObject
{
    public static readonly IReadOnlyList<ColorOption> Palette =
    [
        new("#D97757", "Naranja"),
        new("#4A7FD9", "Azul"),
        new("#1D9E75", "Verde"),
        new("#7F77DD", "Violeta"),
        new("#D4537E", "Rosa"),
        new("#BA7517", "Ámbar"),
        new("#5F5E5A", "Gris"),
    ];

    private readonly Profile? _existing;
    private readonly HashSet<string> _otherNames;
    private readonly Func<string, ProfileFolders> _previewFolders;
    private readonly Func<string?> _pickFolder;
    private readonly Func<string, IReadOnlyList<string>> _folderNotes;
    private string _name;
    private string _initial;
    private bool _initialEdited;
    private ColorOption _selectedColor;
    private string _emailHint;
    private string? _selectedWorkFolder;
    private ProfileFolders? _folders;
    private string? _error;

    /// <param name="existing">Null para un perfil nuevo.</param>
    /// <param name="otherNames">Nombres de los demás perfiles, para no repetirlos.</param>
    /// <param name="previewFolders">Carpetas que tendría un perfil nuevo con ese nombre.</param>
    /// <param name="pickFolder">Abre el selector de carpetas; null si el usuario cancela.</param>
    /// <param name="folderNotes">Avisos para una carpeta de trabajo (archivos compartidos del proyecto, otros perfiles que la usan).</param>
    public ProfileEditorViewModel(Profile? existing, IEnumerable<string> otherNames,
        Func<string, ProfileFolders> previewFolders, Func<string?> pickFolder,
        Func<string, IReadOnlyList<string>>? folderNotes = null)
    {
        _folderNotes = folderNotes ?? (_ => []);
        _existing = existing;
        _otherNames = new HashSet<string>(otherNames.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        _previewFolders = previewFolders;
        _pickFolder = pickFolder;

        _name = existing?.Name ?? "";
        _initial = existing?.IconGlyph ?? "";
        _initialEdited = !string.IsNullOrWhiteSpace(_initial);
        _emailHint = existing?.EmailHint ?? "";
        WorkFolders = new ObservableCollection<string>(existing?.WorkFolders ?? []);

        var colors = Palette.ToList();
        var current = existing?.IconColor;
        if (current is not null && colors.All(c => !string.Equals(c.Hex, current, StringComparison.OrdinalIgnoreCase)))
        {
            colors.Add(new ColorOption(current, "Actual"));
        }
        Colors = colors;
        _selectedColor = current is null
            ? Palette[2]
            : colors.First(c => string.Equals(c.Hex, current, StringComparison.OrdinalIgnoreCase));

        if (existing is { Type: not ProfileType.Main })
        {
            _folders = new ProfileFolders(existing.DesktopDataDir ?? "", existing.ClaudeConfigDir ?? "");
        }
        else if (existing is null)
        {
            RefreshPreview();
        }

        RefreshFolderNotes();
        WorkFolders.CollectionChanged += (_, _) => RefreshFolderNotes();
        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveFolderCommand = new RelayCommand(RemoveFolder, () => SelectedWorkFolder is not null);
    }

    public bool IsNew => _existing is null;

    public bool IsMain => _existing?.Type == ProfileType.Main;

    public string Title => IsNew ? "Nuevo perfil" : $"Editar «{_existing!.Name}»";

    public IReadOnlyList<ColorOption> Colors { get; }

    public ObservableCollection<string> WorkFolders { get; }

    /// <summary>Avisos sobre las carpetas de trabajo.</summary>
    public ObservableCollection<string> FolderNotes { get; } = [];

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand RemoveFolderCommand { get; }

    /// <summary>Lo que hay que guardar; solo tiene valor tras un <see cref="TryAccept"/> correcto.</summary>
    public ProfileEdit? Result { get; private set; }

    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value))
            {
                return;
            }
            if (!_initialEdited)
            {
                _initial = SuggestInitial(value);
                OnPropertyChanged(nameof(Initial));
                OnPropertyChanged(nameof(PreviewInitial));
            }
            if (IsNew)
            {
                RefreshPreview();
            }
            Error = null;
        }
    }

    public string Initial
    {
        get => _initial;
        set
        {
            if (SetProperty(ref _initial, value))
            {
                _initialEdited = !string.IsNullOrWhiteSpace(value);
                OnPropertyChanged(nameof(PreviewInitial));
                Error = null;
            }
        }
    }

    /// <summary>La inicial que se verá en el círculo (la sugerida si el campo está vacío).</summary>
    public string PreviewInitial => string.IsNullOrWhiteSpace(_initial) ? SuggestInitial(_name) : _initial.Trim().ToUpperInvariant();

    public ColorOption SelectedColor
    {
        get => _selectedColor;
        set => SetProperty(ref _selectedColor, value);
    }

    public string EmailHint
    {
        get => _emailHint;
        set => SetProperty(ref _emailHint, value);
    }

    public string? SelectedWorkFolder
    {
        get => _selectedWorkFolder;
        set
        {
            if (SetProperty(ref _selectedWorkFolder, value))
            {
                RemoveFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ShowsDataFolders => !IsMain;

    public string DataDir => _folders?.DesktopDataDir ?? "";

    public string ConfigDir => _folders?.ClaudeConfigDir ?? "";

    public string DataFoldersNote => IsNew
        ? "Claude las crea la primera vez que abres el perfil. No cambian aunque renombres el perfil."
        : "Son fijas: aunque cambies el nombre, el perfil sigue usando estas carpetas.";

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Comprueba el formulario. Si está bien, deja el cambio en <see cref="Result"/>.</summary>
    public bool TryAccept()
    {
        var name = _name.Trim();
        if (name.Length == 0)
        {
            return Fail("Escribe un nombre para el perfil.");
        }
        if (name.Length > ProfileValidator.MaxNameLength)
        {
            return Fail($"El nombre no puede pasar de {ProfileValidator.MaxNameLength} caracteres.");
        }
        if (name.Any(char.IsControl))
        {
            return Fail("El nombre tiene caracteres no válidos.");
        }
        if (_otherNames.Contains(name))
        {
            return Fail("Ya hay un perfil con ese nombre.");
        }

        var initial = PreviewInitial;
        if (new StringInfo(initial).LengthInTextElements != 1)
        {
            return Fail("La inicial debe ser una sola letra o cifra.");
        }

        Result = new ProfileEdit(name, _selectedColor.Hex, initial, _emailHint.Trim(), WorkFolders.ToList());
        Error = null;
        return true;
    }

    private bool Fail(string message)
    {
        Error = message;
        Result = null;
        return false;
    }

    private void AddFolder()
    {
        var folder = _pickFolder();
        if (folder is null || !Path.IsPathFullyQualified(folder))
        {
            return;
        }
        if (WorkFolders.Any(f => string.Equals(f.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        WorkFolders.Add(folder);
    }

    private void RemoveFolder()
    {
        if (SelectedWorkFolder is { } folder)
        {
            WorkFolders.Remove(folder);
            SelectedWorkFolder = null;
        }
    }

    private void RefreshFolderNotes()
    {
        FolderNotes.Clear();
        foreach (var note in WorkFolders.SelectMany(f => _folderNotes(f)))
        {
            FolderNotes.Add(note);
        }
    }

    private void RefreshPreview()
    {
        _folders = string.IsNullOrWhiteSpace(_name) ? null : _previewFolders(_name);
        OnPropertyChanged(nameof(DataDir));
        OnPropertyChanged(nameof(ConfigDir));
    }

    /// <summary>"Claude Trabajo" → "T" (la primera letra de lo que no es "Claude").</summary>
    private static string SuggestInitial(string name) =>
        string.IsNullOrWhiteSpace(name) ? "?" : ProfileFolderNames.BaseName(name)[..1].ToUpperInvariant();
}

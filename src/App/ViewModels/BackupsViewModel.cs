using System.Collections.ObjectModel;
using System.IO;
using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.Core.Backup;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Updates;

namespace ClaudeMultiAccount.App.ViewModels;

/// <summary>Informe de la comprobación de compatibilidad.</summary>
public sealed class CompatibilityReportViewModel(IReadOnlyList<CheckItem> items)
{
    public IReadOnlyList<CheckItem> Items { get; } = items;

    public bool AllOk => Items.All(i => i.Ok);

    public string Summary => AllOk
        ? "Todo está donde se esperaba. Para terminar, abre un perfil aislado y comprueba que sigue con su sesión y sus chats."
        : "Hay algo que no está donde se esperaba. No abras los perfiles marcados hasta revisarlo; puede que esta versión de Claude haya cambiado dónde guarda sus datos.";
}

public sealed class BackupRow(BackupInfo info)
{
    public BackupInfo Info { get; } = info;

    public string Label => Info.Manifest.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    public string Detail => (string.IsNullOrWhiteSpace(Info.Manifest.Note) ? "" : Info.Manifest.Note + " · ") +
                            $"{Info.Manifest.Entries.Count} archivo(s)" +
                            (Info.Manifest.Skipped.Count > 0 ? $" · {Info.Manifest.Skipped.Count} omitido(s)" : "");
}

public sealed class RestoreRow(RestoreCandidate candidate) : ObservableObject
{
    private bool _isSelected = candidate.State != RestoreState.Same;

    public RestoreCandidate Candidate { get; } = candidate;

    public string Description => BackupService.Describe(Candidate.Entry);

    public string StateLabel => Candidate.State switch
    {
        RestoreState.Same => "Igual que ahora",
        RestoreState.Changed => "Ha cambiado desde la copia",
        _ => "Ahora no existe (se volverá a crear)",
    };

    /// <summary>Lo igual no hace falta restaurarlo.</summary>
    public bool CanSelect => Candidate.State != RestoreState.Same;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value && CanSelect);
    }
}

/// <summary>
/// Copias de seguridad de la configuración no sensible: crear, ver qué cambiaría y restaurar
/// lo elegido, siempre guardando antes una copia de lo que se reemplaza.
/// </summary>
public sealed class BackupsViewModel : ObservableObject
{
    private readonly IProfileService _service;
    private readonly IDialogService _dialogs;
    private BackupRow? _selected;
    private string? _message;
    private bool _busy;

    public BackupsViewModel(IProfileService service, IDialogService dialogs)
    {
        _service = service;
        _dialogs = dialogs;
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !_busy);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => !_busy && Rows.Any(r => r.IsSelected));
    }

    public ObservableCollection<BackupRow> Backups { get; } = [];

    public ObservableCollection<RestoreRow> Rows { get; } = [];

    public ObservableCollection<string> Skipped { get; } = [];

    public AsyncRelayCommand CreateCommand { get; }

    public AsyncRelayCommand RestoreCommand { get; }

    public string FolderText => "Se guardan en " + _service.BackupsFolderDisplayPath;

    public string WhatIsCopied =>
        "Se copia solo la lista de perfiles y, de cada perfil, los ajustes (settings.json) y las instrucciones personales (CLAUDE.md) de Claude Code. " +
        "Nunca se copian sesiones, credenciales, chats, historiales ni los ajustes de Claude Desktop. Un archivo que parezca contener una clave se omite.";

    public BackupRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                _ = LoadPreviewAsync();
            }
        }
    }

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public async Task LoadAsync()
    {
        try
        {
            var list = await Task.Run(_service.ListBackups);
            Backups.Clear();
            foreach (var backup in list)
            {
                Backups.Add(new BackupRow(backup));
            }
            Selected = Backups.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = "No se pudieron leer las copias: " + ex.Message;
        }
    }

    private async Task CreateAsync()
    {
        SetBusy(true);
        try
        {
            var backup = await Task.Run(_service.CreateBackup);
            await LoadAsync();
            Selected = Backups.FirstOrDefault(b => b.Info.Folder == backup.Folder);
            Message = $"Copia creada con {backup.Manifest.Entries.Count} archivo(s)" +
                      (backup.Manifest.Skipped.Count > 0 ? $"; {backup.Manifest.Skipped.Count} omitido(s), mira el motivo abajo." : ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProfileStoreException)
        {
            Message = "No se creó la copia: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadPreviewAsync()
    {
        Rows.Clear();
        Skipped.Clear();
        if (_selected is not { } row)
        {
            RestoreCommand.RaiseCanExecuteChanged();
            return;
        }
        foreach (var line in row.Info.Manifest.Skipped)
        {
            Skipped.Add(line);
        }
        try
        {
            var preview = await Task.Run(() => _service.PreviewRestore(row.Info));
            if (!ReferenceEquals(row, _selected))
            {
                return;
            }
            foreach (var candidate in preview)
            {
                var restoreRow = new RestoreRow(candidate);
                restoreRow.PropertyChanged += (_, _) => RestoreCommand.RaiseCanExecuteChanged();
                Rows.Add(restoreRow);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = "No se pudo comparar la copia con los archivos actuales: " + ex.Message;
        }
        RestoreCommand.RaiseCanExecuteChanged();
    }

    private async Task RestoreAsync()
    {
        if (_selected is not { } row)
        {
            return;
        }
        var chosen = Rows.Where(r => r.IsSelected).Select(r => r.Candidate.Entry).ToList();
        var list = string.Join(Environment.NewLine, chosen.Select(e => "   • " + BackupService.Describe(e)));
        if (!_dialogs.ConfirmDangerous("Restaurar copia",
                $"Se reemplazarán estos archivos por los de la copia del {row.Label}:{Environment.NewLine}{list}{Environment.NewLine}{Environment.NewLine}" +
                "Antes se guardará una copia de los archivos actuales, para poder volver atrás." +
                $"{Environment.NewLine}{Environment.NewLine}¿Restaurar?"))
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await Task.Run(() => _service.Restore(row.Info, chosen));
            Message = $"Restaurados {result.Restored} archivo(s)." +
                      (result.SafetyBackupFolder is null ? "" : " Los anteriores están en una copia nueva («Copia automática antes de restaurar»).") +
                      (result.Errors.Count > 0 ? " No se restauró: " + string.Join(" ", result.Errors) : "");
            var current = row.Info.Folder;
            await LoadAsync();
            Selected = Backups.FirstOrDefault(b => b.Info.Folder == current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = "No se pudo restaurar: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CreateCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
    }
}

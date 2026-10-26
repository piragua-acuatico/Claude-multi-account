using ClaudeMultiAccount.App.ViewModels;
using ClaudeMultiAccount.Core.Backup;

namespace ClaudeMultiAccount.App.Tests;

public class BackupsViewModelTests
{
    private readonly FakeProfileService _service = new();
    private readonly FakeDialogService _dialogs = new(answer: true);

    private static BackupEntry Entry(BackupItemKind kind, string name = "Claude Max") =>
        new(kind, Guid.NewGuid(), name, "%USERPROFILE%\\.claude\\x", "01-x", "H");

    [Fact]
    public async Task Create_AddsTheBackupAndExplainsWhatWasSkipped()
    {
        var vm = new BackupsViewModel(_service, _dialogs);
        await vm.LoadAsync();
        Assert.Empty(vm.Backups);

        await vm.CreateCommand.ExecuteAsync();

        var row = Assert.Single(vm.Backups);
        Assert.Same(row, vm.Selected);
        Assert.Contains("1 omitido", vm.Message);
        Assert.Contains(vm.Skipped, s => s.Contains("Claude Pro"));
    }

    [Fact]
    public async Task Preview_PreselectsOnlyWhatChanged()
    {
        var backup = _service.CreateBackup();
        _service.Previews[backup.Folder] =
        [
            new RestoreCandidate(Entry(BackupItemKind.CodeMemory), RestoreState.Changed),
            new RestoreCandidate(Entry(BackupItemKind.CodeSettings), RestoreState.Same),
            new RestoreCandidate(Entry(BackupItemKind.ToolProfiles), RestoreState.Missing),
        ];
        var vm = new BackupsViewModel(_service, _dialogs);

        await vm.LoadAsync();
        await WaitForRows(vm, 3);

        Assert.Equal(new[] { true, false, true }, vm.Rows.Select(r => r.IsSelected));
        Assert.False(vm.Rows[1].CanSelect);
        vm.Rows[1].IsSelected = true;
        Assert.False(vm.Rows[1].IsSelected);
        Assert.Equal("Ahora no existe (se volverá a crear)", vm.Rows[2].StateLabel);
    }

    [Fact]
    public async Task Restore_AsksFirst_ThenRestoresOnlyTheMarkedFiles()
    {
        var backup = _service.CreateBackup();
        var memory = Entry(BackupItemKind.CodeMemory);
        var settings = Entry(BackupItemKind.CodeSettings);
        _service.Previews[backup.Folder] =
        [
            new RestoreCandidate(memory, RestoreState.Changed),
            new RestoreCandidate(settings, RestoreState.Changed),
        ];
        var vm = new BackupsViewModel(_service, _dialogs);
        await vm.LoadAsync();
        await WaitForRows(vm, 2);
        vm.Rows[1].IsSelected = false;

        await vm.RestoreCommand.ExecuteAsync();

        var (title, message) = Assert.Single(_dialogs.Asked);
        Assert.Contains("Restaurar", title);
        Assert.Contains("se guardará una copia de los archivos actuales", message);
        var (_, restored) = Assert.Single(_service.Restores);
        Assert.Equal(new[] { memory }, restored);
        Assert.Contains("Restaurados 1", vm.Message);
    }

    [Fact]
    public async Task Restore_Declined_DoesNothing()
    {
        _dialogs.Answer = false;
        _service.CreateBackup();
        var vm = new BackupsViewModel(_service, _dialogs);
        await vm.LoadAsync();
        await WaitForRows(vm, 1);

        await vm.RestoreCommand.ExecuteAsync();

        Assert.Empty(_service.Restores);
    }

    [Fact]
    public void Report_AllOkOnlyWhenEveryCheckPasses()
    {
        Assert.True(new CompatibilityReportViewModel([new("a", true, "")]).AllOk);
        var bad = new CompatibilityReportViewModel([new("a", true, ""), new("b", false, "")]);
        Assert.False(bad.AllOk);
        Assert.Contains("No abras", bad.Summary);
    }

    /// <summary>La vista previa se carga en segundo plano al elegir una copia.</summary>
    private static async Task WaitForRows(BackupsViewModel vm, int count)
    {
        for (var i = 0; i < 250 && vm.Rows.Count < count; i++)
        {
            await Task.Delay(20);
        }
        Assert.Equal(count, vm.Rows.Count);
    }
}

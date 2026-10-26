using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.App.ViewModels;
using ClaudeMultiAccount.Core.Profiles;
using ClaudeMultiAccount.Core.Workspace;

namespace ClaudeMultiAccount.App.Tests;

public class FolderConflictTests
{
    private const string Web = @"C:\proyectos\web";
    private static readonly StartupOptions NoArgs = new(LaunchProfile: null);
    private readonly FakeProfileService _service = new();
    private readonly FakeDialogService _dialogs = new(answer: true);
    private readonly MainViewModel _vm;
    private readonly Profile _main = TestProfiles.MainProfile();
    private readonly Profile _pro = TestProfiles.Pro();

    public FolderConflictTests()
    {
        _main.WorkFolders = [Web];
        _pro.WorkFolders = [Web];
        _service.Document = TestProfiles.Document(_main, _pro);
        _service.Statuses[_main.Id] = FakeProfileService.RunningOutside();
        _vm = new MainViewModel(_service, _dialogs);
    }

    private ProfileItemViewModel Item(Guid id) => _vm.Profiles.Single(p => p.Id == id);

    [Fact]
    public async Task OpeningInAFolderAnotherOpenProfileUses_Warns()
    {
        _service.SharedFiles[Web] = ["CLAUDE.md", @".claude\"];
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(_pro.Id));

        var warning = Assert.Single(_dialogs.ConflictsShown);
        Assert.Contains("Claude Max", Assert.Single(warning.ConflictLines));
        Assert.Contains("CLAUDE.md", Assert.Single(warning.SharedFileLines));
        Assert.False(warning.CanCreateWorktree);
        Assert.Contains("no es un repositorio", warning.WorktreeText);
        Assert.Empty(_service.Launched);
        Assert.Contains("No se abrió", _vm.StatusMessage);
    }

    [Fact]
    public async Task OpenAnyway_Launches()
    {
        _dialogs.ConflictChoice = FolderConflictChoice.OpenAnyway;
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(_pro.Id));

        Assert.Equal(new[] { _pro.Id }, _service.Launched);
    }

    [Fact]
    public async Task CreateWorktree_MovesTheProfileToItsOwnCopy_ThenLaunches()
    {
        _service.GitRepos.Add(Web);
        _dialogs.ConflictChoice = FolderConflictChoice.CreateWorktree;
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(_pro.Id));

        var warning = Assert.Single(_dialogs.ConflictsShown);
        Assert.True(warning.CanCreateWorktree);
        Assert.Contains(@"C:\proyectos\web-pro", warning.WorktreeText);
        Assert.Contains("multi/pro", warning.WorktreeText);
        var (id, proposal) = Assert.Single(_service.WorktreesCreated);
        Assert.Equal(_pro.Id, id);
        Assert.Equal(@"C:\proyectos\web-pro", proposal.WorktreePath);
        Assert.Equal(new[] { @"C:\proyectos\web-pro" }, Item(_pro.Id).Profile.WorkFolders);
        Assert.Equal(new[] { _pro.Id }, _service.Launched);
    }

    [Fact]
    public async Task FailedWorktree_DoesNotLaunch()
    {
        _service.GitRepos.Add(Web);
        _service.WorktreeError = "Git no pudo crear la copia de trabajo: ya existe.";
        _dialogs.ConflictChoice = FolderConflictChoice.CreateWorktree;
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(_pro.Id));

        Assert.Empty(_service.Launched);
        Assert.Equal("No se creó la copia de trabajo: Git no pudo crear la copia de trabajo: ya existe.", _vm.StatusMessage);
    }

    [Fact]
    public async Task NoWarning_WhenTheOtherProfileIsClosed()
    {
        _service.Statuses[_main.Id] = FakeProfileService.Closed();
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(_pro.Id));

        Assert.Empty(_dialogs.ConflictsShown);
        Assert.Equal(new[] { _pro.Id }, _service.Launched);
    }

    [Fact]
    public async Task NoWarning_WhenTheProfileIsAlreadyOpen()
    {
        _service.Statuses[_pro.Id] = FakeProfileService.RunningFromTool(10);
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(_pro.Id));

        Assert.Empty(_dialogs.ConflictsShown);
    }

    [Fact]
    public async Task NoWarning_ForDifferentFolders()
    {
        _pro.WorkFolders = [@"C:\proyectos\web-pro"];
        await _vm.InitializeAsync(NoArgs);

        await _vm.OpenAsync(Item(_pro.Id));

        Assert.Empty(_dialogs.ConflictsShown);
    }

    [Fact]
    public async Task Editor_WarnsAboutSharedFilesAndOtherProfilesUsingTheFolder()
    {
        _service.SharedFiles[Web] = [".mcp.json"];
        await _vm.InitializeAsync(NoArgs);
        _dialogs.FillEditor = _ => { };

        await _vm.EditAsync(Item(_pro.Id));

        var notes = _dialogs.Editors.Single().FolderNotes;
        Assert.Contains(notes, n => n.Contains(".mcp.json"));
        Assert.Contains(notes, n => n.Contains("también la usa «Claude Max»"));
    }

    [Fact]
    public void Editor_NotesFollowTheFolderList()
    {
        var notes = new Dictionary<string, IReadOnlyList<string>> { [Web] = ["aviso de web"] };
        var editor = new ProfileEditorViewModel(null, [], name => ProfileFolderNames.For("X"), () => Web,
            folder => notes.GetValueOrDefault(folder, []));

        editor.AddFolderCommand.Execute(null);
        Assert.Equal(new[] { "aviso de web" }, editor.FolderNotes);

        editor.SelectedWorkFolder = Web;
        editor.RemoveFolderCommand.Execute(null);
        Assert.Empty(editor.FolderNotes);
    }
}

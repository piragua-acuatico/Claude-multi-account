using ClaudeMultiAccount.App.Services;
using ClaudeMultiAccount.App.ViewModels;
using ClaudeMultiAccount.Core.Profiles;

namespace ClaudeMultiAccount.App.Tests;

public class ProfileEditorViewModelTests
{
    private static ProfileFolders Preview(string name) => ProfileFolderNames.For(ProfileFolderNames.BaseName(name));

    private static ProfileEditorViewModel NewEditor(string? folder = null, params string[] otherNames) =>
        new(null, otherNames, Preview, () => folder);

    [Fact]
    public void NewProfile_SuggestsInitialAndShowsMultiFolders()
    {
        var editor = NewEditor();
        editor.Name = "Claude Trabajo";

        Assert.True(editor.IsNew);
        Assert.Equal("Nuevo perfil", editor.Title);
        Assert.Equal("T", editor.PreviewInitial);
        Assert.Equal(@"%APPDATA%\Multi-Claude-Trabajo", editor.DataDir);
        Assert.Equal(@"%USERPROFILE%\.multi-claude-trabajo", editor.ConfigDir);
        Assert.True(editor.ShowsDataFolders);
    }

    [Fact]
    public void TypedInitial_IsKeptWhenTheNameChanges()
    {
        var editor = NewEditor();
        editor.Name = "Claude Trabajo";
        editor.Initial = "w";
        editor.Name = "Claude Oficina";

        Assert.Equal("W", editor.PreviewInitial);
    }

    [Fact]
    public void TryAccept_BuildsTheEdit()
    {
        var editor = NewEditor(@"C:\proyectos\web");
        editor.Name = "  Claude Trabajo  ";
        editor.SelectedColor = ProfileEditorViewModel.Palette[1];
        editor.EmailHint = " trabajo@empresa.com ";
        editor.AddFolderCommand.Execute(null);
        editor.AddFolderCommand.Execute(null);

        Assert.True(editor.TryAccept());
        var edit = editor.Result!;
        Assert.Equal("Claude Trabajo", edit.Name);
        Assert.Equal("#4A7FD9", edit.IconColor);
        Assert.Equal("T", edit.IconGlyph);
        Assert.Equal("trabajo@empresa.com", edit.EmailHint);
        Assert.Equal(new[] { @"C:\proyectos\web" }, edit.WorkFolders);
    }

    [Theory]
    [InlineData("", "Escribe un nombre")]
    [InlineData("   ", "Escribe un nombre")]
    [InlineData("claude pro", "Ya hay un perfil con ese nombre")]
    public void TryAccept_RejectsInvalidNames(string name, string expectedError)
    {
        var editor = NewEditor(null, "Claude Pro");
        editor.Name = name;

        Assert.False(editor.TryAccept());
        Assert.Null(editor.Result);
        Assert.StartsWith(expectedError, editor.Error);
    }

    [Fact]
    public void TryAccept_RejectsTwoCharacterInitial()
    {
        var editor = NewEditor();
        editor.Name = "Claude Trabajo";
        editor.Initial = "TR";

        Assert.False(editor.TryAccept());
        Assert.Contains("una sola letra", editor.Error);
    }

    [Fact]
    public void CancelledFolderPicker_AddsNothing()
    {
        var editor = NewEditor(folder: null);
        editor.AddFolderCommand.Execute(null);
        Assert.Empty(editor.WorkFolders);
    }

    [Fact]
    public void RemoveFolder_RemovesTheSelectedOne()
    {
        var editor = NewEditor(@"C:\a");
        editor.AddFolderCommand.Execute(null);
        Assert.False(editor.RemoveFolderCommand.CanExecute(null));

        editor.SelectedWorkFolder = @"C:\a";
        editor.RemoveFolderCommand.Execute(null);

        Assert.Empty(editor.WorkFolders);
    }

    [Fact]
    public void EditingAnIsolatedProfile_ShowsItsOwnFixedFolders()
    {
        var pro = TestProfiles.Pro();
        var editor = new ProfileEditorViewModel(pro, [], _ => throw new InvalidOperationException("no se calcula"), () => null);
        editor.Name = "Otro nombre";

        Assert.Equal("Editar «Claude Pro»", editor.Title);
        Assert.Equal(pro.DesktopDataDir, editor.DataDir);
        Assert.Equal("P", editor.PreviewInitial);
        Assert.Equal("#4A7FD9", editor.SelectedColor.Hex);
    }

    [Fact]
    public void EditingTheMainProfile_HidesDataFolders()
    {
        var editor = new ProfileEditorViewModel(TestProfiles.MainProfile(), [], Preview, () => null);
        Assert.True(editor.IsMain);
        Assert.False(editor.ShowsDataFolders);
    }

    [Fact]
    public void UnknownExistingColor_IsOfferedToo()
    {
        var pro = TestProfiles.Pro();
        pro.IconColor = "#123456";
        var editor = new ProfileEditorViewModel(pro, [], Preview, () => null);

        Assert.Equal("#123456", editor.SelectedColor.Hex);
        Assert.Contains(editor.Colors, c => c.Hex == "#123456");
    }
}

public class ProfileManagementTests
{
    private static readonly StartupOptions NoArgs = new(LaunchProfile: null);
    private readonly FakeProfileService _service = new();
    private readonly FakeDialogService _dialogs = new(answer: true);
    private readonly MainViewModel _vm;
    private readonly Profile _main = TestProfiles.MainProfile();
    private readonly Profile _pro = TestProfiles.Pro();

    public ProfileManagementTests()
    {
        _service.Document = TestProfiles.Document(_main, _pro);
        _vm = new MainViewModel(_service, _dialogs);
    }

    private ProfileItemViewModel Item(Guid id) => _vm.Profiles.Single(p => p.Id == id);

    [Fact]
    public async Task NewProfile_CreatesItAndShowsItInTheList()
    {
        await _vm.InitializeAsync(NoArgs);
        _dialogs.FillEditor = e => e.Name = "Claude Trabajo";

        await _vm.NewProfileAsync();

        var created = Assert.Single(_service.Created);
        Assert.Equal("Claude Trabajo", created.Name);
        Assert.Contains(_vm.Profiles, p => p.Name == "Claude Trabajo");
        Assert.Contains("inicia sesión con su cuenta", _vm.StatusMessage);
    }

    [Fact]
    public async Task NewProfile_Cancelled_CreatesNothing()
    {
        await _vm.InitializeAsync(NoArgs);

        await _vm.NewProfileAsync();

        Assert.Single(_dialogs.Editors);
        Assert.Empty(_service.Created);
    }

    [Fact]
    public async Task NewProfile_EditorKnowsExistingNames()
    {
        await _vm.InitializeAsync(NoArgs);
        _dialogs.FillEditor = e => e.Name = "CLAUDE PRO";

        await _vm.NewProfileAsync();

        Assert.Empty(_service.Created);
        Assert.Equal("Ya hay un perfil con ese nombre.", _dialogs.Editors.Single().Error);
    }

    [Fact]
    public async Task NewProfile_SaveFails_ShowsWhy()
    {
        await _vm.InitializeAsync(NoArgs);
        _service.SaveError = "No se guardó profiles.json: carpeta compartida.";
        _dialogs.FillEditor = e => e.Name = "Claude Trabajo";

        await _vm.NewProfileAsync();

        Assert.Contains("carpeta compartida", _vm.StatusMessage);
    }

    [Fact]
    public async Task Edit_SavesChangesAndAllowsKeepingItsOwnName()
    {
        await _vm.InitializeAsync(NoArgs);
        _dialogs.FillEditor = e => e.SelectedColor = ProfileEditorViewModel.Palette[2];

        await _vm.EditAsync(Item(_pro.Id));

        var (id, edit) = Assert.Single(_service.Updated);
        Assert.Equal(_pro.Id, id);
        Assert.Equal("Claude Pro", edit.Name);
        Assert.Equal("#1D9E75", edit.IconColor);
        Assert.Equal("#1D9E75", Item(_pro.Id).Color);
    }

    [Fact]
    public async Task Remove_AsksExplainingNothingIsDeleted_ThenRemoves()
    {
        await _vm.InitializeAsync(NoArgs);

        await _vm.RemoveAsync(Item(_pro.Id));

        var (title, message) = Assert.Single(_dialogs.Asked);
        Assert.Contains("Claude Pro", title);
        Assert.Contains("No se borra nada", message);
        Assert.Contains(_pro.DesktopDataDir!, message);
        Assert.Equal(new[] { _pro.Id }, _service.Removed);
        Assert.DoesNotContain(_vm.Profiles, p => p.Id == _pro.Id);
    }

    [Fact]
    public async Task Remove_Declined_KeepsTheProfile()
    {
        _dialogs.Answer = false;
        await _vm.InitializeAsync(NoArgs);

        await _vm.RemoveAsync(Item(_pro.Id));

        Assert.Empty(_service.Removed);
    }

    [Fact]
    public async Task Remove_NotAllowedForMainOrRunningProfiles()
    {
        _service.Statuses[_pro.Id] = FakeProfileService.RunningFromTool(10);
        await _vm.InitializeAsync(NoArgs);

        Assert.False(Item(_main.Id).CanRemove);
        Assert.False(Item(_pro.Id).CanRemove);
        Assert.Contains("Cierra el perfil", Item(_pro.Id).RemoveToolTip);

        await _vm.RemoveAsync(Item(_pro.Id));
        await _vm.RemoveAsync(Item(_main.Id));
        Assert.Empty(_dialogs.Asked);
        Assert.Empty(_service.Removed);
    }
}

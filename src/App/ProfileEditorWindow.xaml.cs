using System.Windows;
using ClaudeMultiAccount.App.ViewModels;

namespace ClaudeMultiAccount.App;

public partial class ProfileEditorWindow : Window
{
    private readonly ProfileEditorViewModel _editor;

    public ProfileEditorWindow(ProfileEditorViewModel editor)
    {
        InitializeComponent();
        _editor = editor;
        DataContext = editor;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Si el formulario no es válido, la ventana sigue abierta y muestra el motivo.
        if (_editor.TryAccept())
        {
            DialogResult = true;
        }
    }
}

using System.Windows;
using ClaudeMultiAccount.Core.Shortcuts;

namespace ClaudeMultiAccount.App;

public partial class ShortcutLocationWindow : Window
{
    public ShortcutLocationWindow(string profileName)
    {
        InitializeComponent();
        Question.Text = $"¿Dónde quieres el acceso directo de «{profileName}»?";
    }

    /// <summary>La elección del usuario; solo tiene sentido si ShowDialog devolvió true.</summary>
    public ShortcutLocation Location { get; private set; }

    private void OnDesktop(object sender, RoutedEventArgs e) => Choose(ShortcutLocation.Desktop);

    private void OnStartMenu(object sender, RoutedEventArgs e) => Choose(ShortcutLocation.StartMenu);

    private void Choose(ShortcutLocation location)
    {
        Location = location;
        DialogResult = true;
    }
}

using System.Windows;
using ClaudeMultiAccount.App.ViewModels;

namespace ClaudeMultiAccount.App;

public partial class FolderConflictWindow : Window
{
    public FolderConflictWindow(FolderConflictViewModel warning)
    {
        InitializeComponent();
        DataContext = warning;
    }

    /// <summary>Lo que eligió el usuario; solo tiene sentido si ShowDialog devolvió true.</summary>
    public FolderConflictChoice Choice { get; private set; } = FolderConflictChoice.Cancel;

    private void OnOpenAnyway(object sender, RoutedEventArgs e) => Choose(FolderConflictChoice.OpenAnyway);

    private void OnCreateWorktree(object sender, RoutedEventArgs e) => Choose(FolderConflictChoice.CreateWorktree);

    private void Choose(FolderConflictChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}

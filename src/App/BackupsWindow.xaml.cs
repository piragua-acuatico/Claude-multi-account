using System.Windows;
using ClaudeMultiAccount.App.ViewModels;

namespace ClaudeMultiAccount.App;

public partial class BackupsWindow : Window
{
    public BackupsWindow(BackupsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

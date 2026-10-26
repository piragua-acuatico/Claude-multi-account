using System.Windows;
using ClaudeMultiAccount.App.ViewModels;

namespace ClaudeMultiAccount.App;

public partial class UninstallWindow : Window
{
    public UninstallWindow(UninstallViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnUninstall(object sender, RoutedEventArgs e) => DialogResult = true;
}

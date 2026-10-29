using System.Windows;
using ClaudeMultiAccount.App.ViewModels;

namespace ClaudeMultiAccount.App;

public partial class MessagesWindow : Window
{
    public MessagesWindow(MessagesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _ = viewModel.LoadAsync();
    }
}

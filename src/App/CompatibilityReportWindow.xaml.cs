using System.Windows;
using ClaudeMultiAccount.App.ViewModels;

namespace ClaudeMultiAccount.App;

public partial class CompatibilityReportWindow : Window
{
    public CompatibilityReportWindow(CompatibilityReportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

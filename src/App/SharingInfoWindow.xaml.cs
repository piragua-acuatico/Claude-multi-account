using System.Windows;
using ClaudeMultiAccount.App.ViewModels;

namespace ClaudeMultiAccount.App;

public partial class SharingInfoWindow : Window
{
    public SharingInfoWindow(SharingInfoViewModel info)
    {
        InitializeComponent();
        DataContext = info;
    }
}

using System.Windows;
using CdrGraph.Desktop.ViewModels;

namespace CdrGraph.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        // تنظیم DataContext به صورت دستی اینجا انجام می‌شود
        DataContext = viewModel;
    }
}
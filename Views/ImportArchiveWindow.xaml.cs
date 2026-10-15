using System.Windows;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class ImportArchiveWindow : Window
{
    public ImportArchiveViewModel ViewModel { get; }

    public ImportArchiveWindow(ImportArchiveViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

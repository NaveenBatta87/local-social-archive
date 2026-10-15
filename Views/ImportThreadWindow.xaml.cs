using System.Windows;
using System.Windows.Input;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class ImportThreadWindow : Window
{
    private readonly ImportThreadViewModel _viewModel;

    public ImportThreadWindow(ImportThreadViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.RequestClose += () =>
        {
            DialogResult = true;
            Close();
        };

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

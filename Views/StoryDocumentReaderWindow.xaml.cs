using System.Windows;
using System.Windows.Input;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class StoryDocumentReaderWindow : Window
{
    public StoryDocumentReaderWindow(StoryDocumentReaderViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () => Close();
        viewModel.RequestScrollToTop += () =>
        {
            ReaderScrollViewer.ScrollToTop();
        };
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

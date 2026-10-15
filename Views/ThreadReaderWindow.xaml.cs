using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class ThreadReaderWindow : Window
{
    private readonly ThreadReaderViewModel _viewModel;

    public ICommand CloseCommand { get; }

    public ThreadReaderWindow(ThreadReaderViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        CloseCommand = new RelayCommand(Close);

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ThreadReaderViewModel.ActiveTheme))
            {
                ApplyTheme(_viewModel.ActiveTheme);
            }
        };

        ApplyTheme(_viewModel.ActiveTheme);
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

    private void ApplyTheme(string theme)
    {
        switch (theme)
        {
            case "OLED Black":
                Background = new SolidColorBrush(Color.FromRgb(0, 0, 0));
                RootBorder.Background = new SolidColorBrush(Color.FromRgb(0, 0, 0));
                RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(38, 38, 38));
                TopHeaderBorder.Background = new SolidColorBrush(Color.FromRgb(10, 10, 10));
                TopHeaderBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(38, 38, 38));
                Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                break;

            case "Sepia":
                Background = new SolidColorBrush(Color.FromRgb(251, 240, 217));
                RootBorder.Background = new SolidColorBrush(Color.FromRgb(251, 240, 217));
                RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(216, 195, 165));
                TopHeaderBorder.Background = new SolidColorBrush(Color.FromRgb(244, 231, 204));
                TopHeaderBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(216, 195, 165));
                Foreground = new SolidColorBrush(Color.FromRgb(44, 34, 19));
                break;

            case "Dark Slate":
            default:
                Background = new SolidColorBrush(Color.FromRgb(13, 17, 23));
                RootBorder.Background = new SolidColorBrush(Color.FromRgb(13, 17, 23));
                RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61));
                TopHeaderBorder.Background = new SolidColorBrush(Color.FromRgb(22, 27, 34));
                TopHeaderBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61));
                Foreground = new SolidColorBrush(Color.FromRgb(231, 233, 234));
                break;
        }
    }
}

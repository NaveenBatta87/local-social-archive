using System.Windows;
using LocalSocialArchive.Services;
using LocalSocialArchive.ViewModels;
using LocalSocialArchive.Views;

namespace LocalSocialArchive;

public partial class App : Application
{
    private IEseDatabaseService? _dbService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global Exception Handling to ensure application resilience
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Unhandled Error: {ex.Message}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"UI Error: {args.Exception.Message}", "UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Wire Dependencies
        _dbService = new EseDatabaseService();
        var clipboardService = new ClipboardService(_dbService);
        var mainViewModel = new MainViewModel(_dbService, clipboardService);

        var mainWindow = new MainWindow(mainViewModel);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_dbService is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }
}

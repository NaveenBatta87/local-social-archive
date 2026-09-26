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

        // Prevent application from shutting down when UnlockWindow closes before MainWindow opens
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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

        // Initialize Core Security Service
        var securityService = new SecurityService();

        // Check if master password lock is enabled before loading the database/UI
        if (securityService.IsPasswordProtectionEnabled)
        {
            var unlockWindow = new UnlockWindow(securityService);
            bool? isUnlocked = unlockWindow.ShowDialog();
            if (isUnlocked != true)
            {
                Shutdown();
                return;
            }
        }

        // Wire Dependencies
        _dbService = new EseDatabaseService();
        var clipboardService = new ClipboardService(_dbService);
        var mainViewModel = new MainViewModel(_dbService, clipboardService, securityService);

        var mainWindow = new MainWindow(mainViewModel);
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
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

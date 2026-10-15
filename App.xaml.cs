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

        void Log(string msg) => LocalSocialArchive.Helpers.StoragePathHelper.LogDiagnostic(msg);

        Log("--- OnStartup started ---");

        try
        {
            // Enable subpixel rendering for sharp, full-color vector emojis
            Emoji.Wpf.EmojiData.EnableSubPixelRendering = true;
            Log("SubPixel Emoji rendering enabled.");
        }
        catch (Exception ex)
        {
            Log($"[Notice] Could not enable emoji subpixel rendering: {ex.Message}");
        }

        // Prevent application from shutting down when UnlockWindow closes before MainWindow opens
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Global Exception Handling to ensure application resilience
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log($"[CRITICAL UnhandledException] {ex}");
                MessageBox.Show($"Unhandled Error: {ex.Message}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"[CRITICAL DispatcherUnhandledException] {args.Exception}");
            MessageBox.Show($"UI Error: {args.Exception.Message}", "UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            // Initialize Core Security Service
            Log("Initializing SecurityService...");
            var securityService = new SecurityService();
            Log($"SecurityService initialized. IsPasswordProtectionEnabled: {securityService.IsPasswordProtectionEnabled}");

            // Check if master password lock is enabled before loading the database/UI
            if (securityService.IsPasswordProtectionEnabled)
            {
                Log("Opening UnlockWindow dialog...");
                var unlockWindow = new UnlockWindow(securityService);
                bool? isUnlocked = unlockWindow.ShowDialog();
                Log($"UnlockWindow returned: {isUnlocked}");
                if (isUnlocked != true)
                {
                    Log("User did not unlock. Shutting down.");
                    Shutdown();
                    return;
                }
            }

            // Wire Dependencies
            Log("Creating SqliteDatabaseService...");
            _dbService = new SqliteDatabaseService();
            Log("Creating SettingsService, ClipboardService, TweetImporterService...");
            var settingsService = new SettingsService();
            var clipboardService = new ClipboardService(_dbService);
            var tweetImporterService = new TweetImporterService(_dbService);

            Log("Creating MainViewModel...");
            var mainViewModel = new MainViewModel(_dbService, clipboardService, securityService, settingsService, tweetImporterService);

            Log("Creating MainWindow...");
            var mainWindow = new MainWindow(mainViewModel);
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            Log("Showing MainWindow...");
            mainWindow.Show();
            Log("MainWindow shown successfully.");
        }
        catch (Exception ex)
        {
            Log($"[STARTUP EXCEPTION] {ex}");
            MessageBox.Show($"Startup Error: {ex.Message}\n\nStack:\n{ex.StackTrace}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
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

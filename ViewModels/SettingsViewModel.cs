using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSocialArchive.Models;
using LocalSocialArchive.Services;
using LocalSocialArchive.Views;

namespace LocalSocialArchive.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IEseDatabaseService _dbService;
    private readonly ISecurityService _securityService;
    private readonly string _baseDirectory;
    private readonly string _mediaDirectory;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _mediaStoragePath = string.Empty;

    [ObservableProperty]
    private string _databaseSizeText = "Calculating...";

    [ObservableProperty]
    private string _mediaSizeText = "Calculating...";

    [ObservableProperty]
    private string _totalPostsCountText = "0 posts";

    [ObservableProperty]
    private string _ramUsageText = "0 MB";

    [ObservableProperty]
    private string _selectedDefaultFolder = "Coding";

    [ObservableProperty]
    private ObservableCollection<string> _availableFolders = new();

    [ObservableProperty]
    private bool _autoExtractHashtags = true;

    [ObservableProperty]
    private bool _preferGridView;

    [ObservableProperty]
    private bool _showInspectorByDefault = true;

    [ObservableProperty]
    private bool _showTagsByDefault = true;

    [ObservableProperty]
    private bool _isPasswordProtectionEnabled;

    [ObservableProperty]
    private bool _hasPasswordSet;

    [ObservableProperty]
    private string _passwordStatusText = "Disabled";

    [ObservableProperty]
    private string _passwordButtonText = "Set Master Password";

    [ObservableProperty]
    private string _statusNotification = "Settings ready";

    public SettingsViewModel(
        IEseDatabaseService dbService,
        ISecurityService securityService,
        ObservableCollection<FolderCategory> currentFolders,
        bool isGridView,
        bool isInspectorVisible,
        bool isTagsVisible)
    {
        _dbService = dbService;
        _securityService = securityService;

        string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _baseDirectory = Path.Combine(userDocs, "LocalSocialArchive");
        _mediaDirectory = Path.Combine(_baseDirectory, "Media");

        DatabasePath = _dbService.DatabasePath;
        MediaStoragePath = _mediaDirectory;
        PreferGridView = isGridView;
        ShowInspectorByDefault = isInspectorVisible;
        ShowTagsByDefault = isTagsVisible;

        AvailableFolders = new ObservableCollection<string>();
        foreach (var f in currentFolders)
        {
            if (f.Name != "All Posts")
            {
                AvailableFolders.Add(f.Name);
            }
        }
        if (AvailableFolders.Count == 0)
        {
            AvailableFolders.Add("Coding");
        }

        RefreshSecurityState();
        RefreshMetrics();
    }

    public void RefreshSecurityState()
    {
        HasPasswordSet = _securityService.HasPasswordSet;
        IsPasswordProtectionEnabled = _securityService.IsPasswordProtectionEnabled;

        if (IsPasswordProtectionEnabled)
        {
            PasswordStatusText = "🔒 Vault is locked on startup (PBKDF2 SHA-256 Active)";
            PasswordButtonText = "Change / Remove Password";
        }
        else if (HasPasswordSet)
        {
            PasswordStatusText = "🔓 Password configured but startup lock is disabled";
            PasswordButtonText = "Change / Enable Password";
        }
        else
        {
            PasswordStatusText = "🔓 No master password configured (Unrestricted Access)";
            PasswordButtonText = "Set Master Password";
        }
    }

    [RelayCommand]
    public void ConfigurePassword(Window? owner)
    {
        var setPwdWin = new SetPasswordWindow(_securityService)
        {
            Owner = owner ?? Application.Current.MainWindow
        };

        if (setPwdWin.ShowDialog() == true)
        {
            RefreshSecurityState();
            StatusNotification = HasPasswordSet ? "Master password updated successfully!" : "Password removed.";
        }
    }

    [RelayCommand]
    public void ToggleProtection(Window? owner)
    {
        if (!HasPasswordSet)
        {
            ConfigurePassword(owner);
            return;
        }

        _securityService.IsPasswordProtectionEnabled = !_securityService.IsPasswordProtectionEnabled;
        RefreshSecurityState();
        StatusNotification = IsPasswordProtectionEnabled ? "Startup password lock enabled." : "Startup password lock disabled.";
    }

    [RelayCommand]
    public void RefreshMetrics()
    {
        try
        {
            // DB File Size
            if (File.Exists(DatabasePath))
            {
                var fi = new FileInfo(DatabasePath);
                DatabaseSizeText = fi.Length < 1024 * 1024
                    ? $"{fi.Length / 1024.0:F1} KB"
                    : $"{fi.Length / (1024.0 * 1024.0):F2} MB";
            }
            else
            {
                DatabaseSizeText = "0.0 MB";
            }

            // Media Directory Size
            if (Directory.Exists(MediaStoragePath))
            {
                long totalBytes = Directory.GetFiles(MediaStoragePath, "*.*", SearchOption.AllDirectories)
                                           .Sum(f => new FileInfo(f).Length);
                int fileCount = Directory.GetFiles(MediaStoragePath, "*.*", SearchOption.AllDirectories).Length;

                if (totalBytes < 1024 * 1024)
                {
                    MediaSizeText = $"{totalBytes / 1024.0:F1} KB ({fileCount} files)";
                }
                else if (totalBytes < 1024 * 1024 * 1024)
                {
                    MediaSizeText = $"{totalBytes / (1024.0 * 1024.0):F2} MB ({fileCount} files)";
                }
                else
                {
                    MediaSizeText = $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB ({fileCount} files)";
                }
            }
            else
            {
                MediaSizeText = "0.0 MB";
            }

            // Process RAM usage
            using var proc = Process.GetCurrentProcess();
            long ramBytes = proc.WorkingSet64;
            RamUsageText = $"{ramBytes / (1024.0 * 1024.0):F1} MB (Active Working Set)";

            StatusNotification = "Metrics updated successfully.";
        }
        catch (Exception ex)
        {
            StatusNotification = $"Metrics Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenDatabaseFolder()
    {
        try
        {
            if (Directory.Exists(_baseDirectory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_baseDirectory}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open directory:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void OpenMediaFolder()
    {
        try
        {
            if (!Directory.Exists(_mediaDirectory))
            {
                Directory.CreateDirectory(_mediaDirectory);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_mediaDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open directory:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void BackupDatabase()
    {
        try
        {
            if (!File.Exists(DatabasePath))
            {
                MessageBox.Show("Database file does not exist to back up.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string backupFolder = Path.Combine(_baseDirectory, "Backups");
            Directory.CreateDirectory(backupFolder);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(backupFolder, $"ArchiveData_backup_{timestamp}.edb");

            File.Copy(DatabasePath, backupPath, overwrite: true);

            StatusNotification = $"Database backed up to Backups/ folder!";
            MessageBox.Show(
                $"Database backup successfully created at:\n\n{backupPath}",
                "Backup Succeeded",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Open backup folder in Explorer
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{backupPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Backup failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void SaveSettings(Window? window)
    {
        if (window != null)
        {
            window.DialogResult = true;
            window.Close();
        }
    }
}

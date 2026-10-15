using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using LocalSocialArchive.Services;

namespace LocalSocialArchive.ViewModels;

public partial class ImportArchiveViewModel : ObservableObject
{
    private readonly ITweetImporterService _importerService;
    private readonly MainViewModel _mainViewModel;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool _isZipMode = true;

    [ObservableProperty]
    private bool _isGalleryDlMode = false;

    [ObservableProperty]
    private string _archiveZipPath = string.Empty;

    [ObservableProperty]
    private string _targetFolder = "X Archive";

    [ObservableProperty]
    private bool _importLikes = false;

    // Gallery-DL specific
    [ObservableProperty]
    private string _targetUrl = string.Empty;

    [ObservableProperty]
    private string _galleryDlPath = string.Empty;

    [ObservableProperty]
    private string _selectedBrowserCookie = "Edge";

    [ObservableProperty]
    private int _maxTweets = 50;

    public ObservableCollection<string> AvailableBrowserCookies { get; } = new()
    {
        "None",
        "Edge",
        "Chrome",
        "Firefox",
        "Brave"
    };

    // Progress and Logging
    [ObservableProperty]
    private bool _isImporting = false;

    [ObservableProperty]
    private double _progressPercentage = 0;

    [ObservableProperty]
    private string _progressStatusText = "Ready to import";

    [ObservableProperty]
    private string _logOutputText = string.Empty;

    [ObservableProperty]
    private bool _hasLogOutput = false;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    [ObservableProperty]
    private bool? _isSuccessResult = null;

    public ImportArchiveViewModel(ITweetImporterService importerService, MainViewModel mainViewModel)
    {
        _importerService = importerService;
        _mainViewModel = mainViewModel;

        // Auto-detect gallery-dl path
        var detected = _importerService.DetectGalleryDlPath();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            _galleryDlPath = detected;
        }
    }

    [RelayCommand]
    public void SelectTab(string tabName)
    {
        if (tabName == "Zip")
        {
            IsZipMode = true;
            IsGalleryDlMode = false;
            if (string.IsNullOrWhiteSpace(TargetFolder) || TargetFolder == "Downloads")
            {
                TargetFolder = "X Archive";
            }
        }
        else
        {
            IsZipMode = false;
            IsGalleryDlMode = true;
            if (string.IsNullOrWhiteSpace(TargetFolder) || TargetFolder == "X Archive")
            {
                TargetFolder = "Downloads";
            }
        }
    }

    [RelayCommand]
    public void BrowseZipFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Twitter/X Archive (.zip) or JSON File",
            Filter = "Supported Archives (*.zip;*.json)|*.zip;*.json|Twitter Archive Zip (*.zip)|*.zip|Metadata JSON (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            ArchiveZipPath = dialog.FileName;
        }
    }

    [RelayCommand]
    public void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Folder Containing Twitter Archive or Downloaded Media"
        };

        if (dialog.ShowDialog() == true)
        {
            ArchiveZipPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    public void BrowseGalleryDlPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Locate gallery-dl.exe",
            Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            GalleryDlPath = dialog.FileName;
        }
    }

    [RelayCommand]
    public async Task StartImportAsync()
    {
        if (IsImporting) return;

        IsImporting = true;
        ProgressPercentage = 0;
        ProgressStatusText = "Preparing import...";
        LogOutputText = string.Empty;
        HasLogOutput = false;
        ResultMessage = string.Empty;
        IsSuccessResult = null;
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<ImportProgressReport>(report =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ProgressStatusText = report.Status;
                    if (report.Total > 0)
                    {
                        ProgressPercentage = Math.Min(100, Math.Max(0, (double)report.Current / report.Total * 100));
                    }
                });
            });

            ImportResult result;

            if (IsZipMode)
            {
                if (string.IsNullOrWhiteSpace(ArchiveZipPath))
                {
                    ResultMessage = "Please select a Twitter Archive .zip file or folder.";
                    IsSuccessResult = false;
                    IsImporting = false;
                    return;
                }

                if (Directory.Exists(ArchiveZipPath))
                {
                    result = await _importerService.ImportDirectoryAsync(ArchiveZipPath, TargetFolder, progress);
                }
                else if (File.Exists(ArchiveZipPath))
                {
                    if (ArchiveZipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        result = await _importerService.ImportTwitterArchiveZipAsync(ArchiveZipPath, TargetFolder, ImportLikes, progress);
                    }
                    else
                    {
                        // Single JSON file
                        string parentDir = Path.GetDirectoryName(ArchiveZipPath) ?? ArchiveZipPath;
                        result = await _importerService.ImportDirectoryAsync(parentDir, TargetFolder, progress);
                    }
                }
                else
                {
                    ResultMessage = $"Selected path does not exist: {ArchiveZipPath}";
                    IsSuccessResult = false;
                    IsImporting = false;
                    return;
                }
            }
            else
            {
                // Gallery-DL mode
                if (string.IsNullOrWhiteSpace(GalleryDlPath) || !File.Exists(GalleryDlPath))
                {
                    ResultMessage = "gallery-dl.exe was not found. Please click Browse to locate gallery-dl.exe.";
                    IsSuccessResult = false;
                    IsImporting = false;
                    return;
                }

                if (string.IsNullOrWhiteSpace(TargetUrl))
                {
                    ResultMessage = "Please enter a Twitter/X handle (e.g. @username) or profile URL.";
                    IsSuccessResult = false;
                    IsImporting = false;
                    return;
                }

                HasLogOutput = true;
                result = await _importerService.DownloadAndImportWithGalleryDlAsync(
                    GalleryDlPath,
                    TargetUrl,
                    TargetFolder,
                    SelectedBrowserCookie,
                    MaxTweets,
                    progress,
                    logLine =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            LogOutputText += logLine + Environment.NewLine;
                        });
                    },
                    _cts.Token);
            }

            IsSuccessResult = result.Success;
            ResultMessage = result.Message;
            if (result.Warnings.Count > 0)
            {
                ResultMessage += $"\nWarnings: {string.Join("; ", result.Warnings)}";
            }

            if (result.Success && result.ImportedCount > 0)
            {
                ProgressPercentage = 100;
                ProgressStatusText = "Import finished!";
                // Refresh main feed
                await _mainViewModel.RefreshDataAsync();
            }
        }
        catch (Exception ex)
        {
            IsSuccessResult = false;
            ResultMessage = $"Error: {ex.Message}";
            ProgressStatusText = "Import failed";
        }
        finally
        {
            IsImporting = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        if (IsImporting && _cts != null)
        {
            _cts.Cancel();
            ProgressStatusText = "Canceling...";
        }
    }
}

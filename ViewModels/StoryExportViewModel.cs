using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;
using LocalSocialArchive.Services;

namespace LocalSocialArchive.ViewModels;

public partial class StoryExportViewModel : ObservableObject
{
    private readonly IStoryLayoutService _layoutService;
    private readonly IStoryExportService _exportService;
    private readonly List<StoryItem> _sourceItems = new();
    private System.Threading.CancellationTokenSource? _debounceCts;

    [ObservableProperty]
    private string _authorName = "Thread Author";

    [ObservableProperty]
    private string _authorHandle = "@author";

    [ObservableProperty]
    private string _postDateText = string.Empty;

    [ObservableProperty]
    private string _rawInputText = string.Empty;

    [ObservableProperty]
    private bool _isAutoFit = true;

    [ObservableProperty]
    private double _customFontSize = 38.0;

    [ObservableProperty]
    private bool _includeMedia = true;

    [ObservableProperty]
    private int _detectedItemsCount;

    [ObservableProperty]
    private StoryTheme _selectedTheme;

    public ObservableCollection<StoryTheme> AvailableThemes { get; } = new();

    [ObservableProperty]
    private ObservableCollection<StoryCard> _cards = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CardIndicatorText))]
    private int _currentCardIndex = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CardIndicatorText))]
    private int _totalCards = 1;

    [ObservableProperty]
    private StoryCard? _currentCard;

    public bool CanGoPrevious => CurrentCardIndex > 1;
    public bool CanGoNext => CurrentCardIndex < TotalCards;
    public string CardIndicatorText => $"Card {CurrentCardIndex} of {Math.Max(1, TotalCards)}";

    [ObservableProperty]
    private string _statusMessage = "Ready to create 9:16 story cards.";

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string _exportProgressText = string.Empty;

    public event Action? RequestClose;

    public StoryExportViewModel(
        IStoryLayoutService? layoutService = null,
        IStoryExportService? exportService = null,
        IEnumerable<Post>? initialPosts = null)
    {
        _layoutService = layoutService ?? new StoryLayoutService();
        _exportService = exportService ?? new StoryExportService();

        var defaultThemes = StoryTheme.GetDefaultThemes();
        foreach (var t in defaultThemes)
        {
            AvailableThemes.Add(t);
        }
        _selectedTheme = AvailableThemes[0];
        _postDateText = DateTime.Now.ToString("MMMM dd, yyyy • h:mm tt");

        if (initialPosts != null && initialPosts.Any())
        {
            LoadFromPosts(initialPosts);
        }
        else
        {
            TryAutoCheckClipboard();
        }
    }

    private void TryAutoCheckClipboard()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text) &&
                    (text.Contains("1/") || text.Contains("Thread by") || text.Contains("threadreaderapp") || text.Contains("---")))
                {
                    StatusMessage = "Found thread text in clipboard. Click 'Paste Thread' to load.";
                }
            }
        }
        catch { }
    }

    public void LoadFromPosts(IEnumerable<Post> posts)
    {
        var postList = posts.OrderBy(p => p.ThreadIndex).ToList();
        if (postList.Count == 0) return;

        var first = postList[0];
        AuthorName = !string.IsNullOrWhiteSpace(first.AuthorName) ? first.AuthorName : "Author";
        AuthorHandle = !string.IsNullOrWhiteSpace(first.AuthorHandle) ? first.AuthorHandle : "@author";
        PostDateText = first.PostDate.ToString("MMMM dd, yyyy • h:mm tt");

        _sourceItems.Clear();
        int idx = 1;
        foreach (var p in postList)
        {
            string? mediaPath = null;
            if (p.MediaItems.Count > 0)
            {
                mediaPath = p.MediaItems.FirstOrDefault(m => !string.IsNullOrEmpty(m.FilePath) && File.Exists(m.FilePath))?.FilePath;
            }

            _sourceItems.Add(new StoryItem
            {
                SegmentIndex = idx++,
                Content = p.Content,
                MediaPath = mediaPath,
                IncludeMedia = IncludeMedia
            });
        }

        DetectedItemsCount = _sourceItems.Count;
        RebuildCards();
        StatusMessage = $"Loaded {DetectedItemsCount} tweets from Thread Reader.";
    }

    [RelayCommand]
    public void PasteFromClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                StatusMessage = "Clipboard does not contain readable text.";
                return;
            }

            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                StatusMessage = "Clipboard is empty.";
                return;
            }

            RawInputText = text;
            var (author, handle, _, segments) = ThreadParserHelper.ParseThreadRaw(text, ThreadSplitMode.Auto);

            if (!string.IsNullOrWhiteSpace(author) && author != "Offline User")
            {
                AuthorName = author;
            }
            if (!string.IsNullOrWhiteSpace(handle) && handle != "@offline")
            {
                AuthorHandle = handle;
            }

            _sourceItems.Clear();
            int idx = 1;
            foreach (var seg in segments)
            {
                if (!string.IsNullOrWhiteSpace(seg))
                {
                    _sourceItems.Add(new StoryItem
                    {
                        SegmentIndex = idx++,
                        Content = seg.Trim(),
                        IncludeMedia = IncludeMedia
                    });
                }
            }

            DetectedItemsCount = _sourceItems.Count;
            RebuildCards();
            StatusMessage = $"Parsed {DetectedItemsCount} thread segments from clipboard.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Clipboard Error: {ex.Message}";
        }
    }

    private void RebuildCards()
    {
        if (_sourceItems.Count == 0)
        {
            var fallback = _layoutService.GenerateStoryCards(
                new List<StoryItem>(), AuthorName, AuthorHandle, PostDateText, SelectedTheme, IsAutoFit, CustomFontSize, IncludeMedia);
            Cards = new ObservableCollection<StoryCard>(fallback);
        }
        else
        {
            var generated = _layoutService.GenerateStoryCards(
                _sourceItems, AuthorName, AuthorHandle, PostDateText, SelectedTheme, IsAutoFit, CustomFontSize, IncludeMedia);
            Cards = new ObservableCollection<StoryCard>(generated);
        }

        TotalCards = Cards.Count;
        CurrentCardIndex = Math.Clamp(CurrentCardIndex, 1, Math.Max(1, TotalCards));
        CurrentCard = Cards.Count > 0 ? Cards[CurrentCardIndex - 1] : null;

        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CardIndicatorText));
    }

    partial void OnSelectedThemeChanged(StoryTheme value)
    {
        if (value != null)
        {
            foreach (var card in Cards)
            {
                card.Theme = value;
            }
            if (CurrentCard != null)
            {
                CurrentCard.Theme = value;
            }
        }
    }

    partial void OnIsAutoFitChanged(bool value)
    {
        TriggerDebouncedRebuild();
    }

    partial void OnCustomFontSizeChanged(double value)
    {
        if (!IsAutoFit)
        {
            TriggerDebouncedRebuild();
        }
    }

    partial void OnIncludeMediaChanged(bool value)
    {
        TriggerDebouncedRebuild();
    }

    partial void OnAuthorNameChanged(string value)
    {
        foreach (var c in Cards)
        {
            c.AuthorName = value;
            c.AvatarInitial = !string.IsNullOrWhiteSpace(value) ? value.Substring(0, 1).ToUpperInvariant() : "A";
        }
    }

    partial void OnAuthorHandleChanged(string value)
    {
        foreach (var c in Cards)
        {
            c.AuthorHandle = value;
        }
    }

    private void TriggerDebouncedRebuild()
    {
        _debounceCts?.Cancel();
        _debounceCts = new System.Threading.CancellationTokenSource();
        var token = _debounceCts.Token;

        Task.Delay(120, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Application.Current?.Dispatcher.InvokeAsync(RebuildCards);
            }
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    public void NextCard()
    {
        if (CurrentCardIndex < TotalCards)
        {
            CurrentCardIndex++;
            CurrentCard = Cards[CurrentCardIndex - 1];
        }
    }

    [RelayCommand]
    public void PreviousCard()
    {
        if (CurrentCardIndex > 1)
        {
            CurrentCardIndex--;
            CurrentCard = Cards[CurrentCardIndex - 1];
        }
    }

    [RelayCommand]
    public async Task ExportStoryAsync()
    {
        if (Cards.Count == 0)
        {
            MessageBox.Show("No story cards available to export. Please load or paste a thread first.", "Export Story", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Destination Folder for Story PNGs",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;

        string targetDir = dialog.FolderName;
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir)) return;

        IsExporting = true;
        StatusMessage = "Exporting story cards as 1080x1920 PNGs...";

        try
        {
            string baseName = $"{AuthorHandle.TrimStart('@')}_story";
            var progress = new Progress<(int current, int total)>(p =>
            {
                ExportProgressText = $"Saving card {p.current} of {p.total}...";
                StatusMessage = $"Exported card {p.current} / {p.total}";
            });

            var result = await _exportService.ExportCardsAsync(Cards, targetDir, baseName, progress);

            if (result.Success)
            {
                StatusMessage = $"Successfully exported {result.ExportedFiles.Count} story image(s)!";
                var askOpen = MessageBox.Show(
                    $"Successfully exported {result.ExportedFiles.Count} story image(s) (1080 × 1920 PNG) to:\n\n{targetDir}\n\nWould you like to open the output folder now?",
                    "Export Complete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (askOpen == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetDir,
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                StatusMessage = $"Export Failed: {result.ErrorMessage}";
                MessageBox.Show($"Failed to export story cards:\n{result.ErrorMessage}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export Error: {ex.Message}";
            MessageBox.Show($"Error during export:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsExporting = false;
            ExportProgressText = string.Empty;
        }
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke();
    }
}

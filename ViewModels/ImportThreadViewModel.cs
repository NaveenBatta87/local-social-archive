using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;
using LocalSocialArchive.Services;

namespace LocalSocialArchive.ViewModels;

public partial class ImportThreadViewModel : ObservableObject
{
    private readonly IEseDatabaseService _dbService;
    private readonly string _mediaBaseDir;
    private System.Threading.CancellationTokenSource? _debounceCts;

    [ObservableProperty]
    private string _rawInputText = string.Empty;

    [ObservableProperty]
    private string _authorName = "Thread Author";

    [ObservableProperty]
    private string _authorHandle = "@author";

    [ObservableProperty]
    private string _tagsText = "#thread";

    [ObservableProperty]
    private string _targetFolder = "Threads";

    [ObservableProperty]
    private ObservableCollection<string> _availableFolders = new();

    [ObservableProperty]
    private string _selectedSplitMode = "Auto-Detect (Thread Reader / Numbered)";

    public ObservableCollection<string> SplitModeOptions { get; } = new()
    {
        "Auto-Detect (Thread Reader / Numbered)",
        "Numbered (1/, 2/ or 1., 2.)",
        "Dividers (--- or ***)",
        "Double Line Break (Paragraphs)"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSegmentsCount))]
    [NotifyPropertyChangedFor(nameof(HasSegments))]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    private ObservableCollection<ThreadSegmentItem> _segments = new();

    public int TotalSegmentsCount => Segments.Count;
    public bool HasSegments => Segments.Count > 0;
    public string SummaryBadgeText => Segments.Count > 0 ? $"{Segments.Count} Tweets Detected" : "No Tweets Detected";

    [ObservableProperty]
    private bool _isParsing;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _statusText = "Paste a thread from Thread Reader App, Twitter unroll, or text below.";

    public List<Post> ImportedPosts { get; private set; } = new();

    public event Action? RequestClose;

    public ImportThreadViewModel(IEseDatabaseService dbService, IEnumerable<string> existingFolders, string defaultFolder = "Threads")
    {
        _dbService = dbService;
        string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _mediaBaseDir = Path.Combine(userDocs, "LocalSocialArchive", "Media");
        Directory.CreateDirectory(_mediaBaseDir);

        var foldersList = existingFolders.Where(f => !string.IsNullOrWhiteSpace(f) && !string.Equals(f, "All Posts", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        if (!foldersList.Contains("Threads", StringComparer.OrdinalIgnoreCase))
        {
            foldersList.Insert(0, "Threads");
        }
        AvailableFolders = new ObservableCollection<string>(foldersList);
        _targetFolder = AvailableFolders.Contains(defaultFolder) ? defaultFolder : "Threads";

        // Try reading clipboard automatically on launch
        TryAutoPasteClipboard();
    }

    private void TryAutoPasteClipboard()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text) && (text.Contains("1/") || text.Contains("Thread by") || text.Contains("threadreaderapp") || text.Contains("---")))
                {
                    RawInputText = text;
                }
            }
        }
        catch { }
    }

    partial void OnRawInputTextChanged(string value)
    {
        TriggerDebouncedParse();
    }

    partial void OnSelectedSplitModeChanged(string value)
    {
        TriggerDebouncedParse();
    }

    private void TriggerDebouncedParse()
    {
        _debounceCts?.Cancel();
        _debounceCts = new System.Threading.CancellationTokenSource();
        var token = _debounceCts.Token;

        Task.Delay(140, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Application.Current?.Dispatcher.InvokeAsync(ParseInputAsync);
            }
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    public void PasteFromClipboard()
    {
        try
        {
            string? text = null;
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                RawInputText = text;
                StatusText = "Pasted from clipboard.";
            }
            else
            {
                StatusText = "Clipboard does not contain readable text.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Clipboard Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ParseInputAsync()
    {
        if (string.IsNullOrWhiteSpace(RawInputText))
        {
            Segments.Clear();
            StatusText = "Paste text or HTML above to preview the thread segments.";
            return;
        }

        IsParsing = true;
        StatusText = "Analyzing and segmenting thread...";

        try
        {
            var mode = SelectedSplitMode switch
            {
                "Numbered (1/, 2/ or 1., 2.)" => ThreadSplitMode.Numbered,
                "Dividers (--- or ***)" => ThreadSplitMode.Dividers,
                "Double Line Break (Paragraphs)" => ThreadSplitMode.DoubleLineBreak,
                _ => ThreadSplitMode.Auto
            };

            var (author, handle, tags, rawSegments) = await Task.Run(() => ThreadParserHelper.ParseThreadRaw(RawInputText, mode));

            if (!string.IsNullOrWhiteSpace(author) && AuthorName == "Thread Author")
            {
                AuthorName = author;
            }
            if (!string.IsNullOrWhiteSpace(handle) && AuthorHandle == "@author")
            {
                AuthorHandle = handle;
            }
            if (!string.IsNullOrWhiteSpace(tags))
            {
                TagsText = tags;
            }

            var segItems = new ObservableCollection<ThreadSegmentItem>();
            int idx = 1;
            foreach (var seg in rawSegments)
            {
                segItems.Add(new ThreadSegmentItem
                {
                    Index = idx++,
                    Content = seg
                });
            }

            Segments = segItems;
            StatusText = $"Successfully segmented into {Segments.Count} tweets.";
        }
        catch (Exception ex)
        {
            StatusText = $"Parse Error: {ex.Message}";
        }
        finally
        {
            IsParsing = false;
        }
    }

    [RelayCommand]
    public void DeleteSegment(ThreadSegmentItem? item)
    {
        if (item == null) return;
        Segments.Remove(item);

        // Renumber remaining items
        for (int i = 0; i < Segments.Count; i++)
        {
            Segments[i].Index = i + 1;
        }

        OnPropertyChanged(nameof(TotalSegmentsCount));
        OnPropertyChanged(nameof(HasSegments));
        OnPropertyChanged(nameof(SummaryBadgeText));
        StatusText = $"Removed tweet. {Segments.Count} tweets remaining.";
    }

    [RelayCommand]
    public async Task SaveThreadAsync()
    {
        var validSegments = Segments.Where(s => !s.IsExcluded && !string.IsNullOrWhiteSpace(s.Content)).ToList();
        if (validSegments.Count == 0)
        {
            MessageBox.Show("Please ensure there is at least one valid tweet segment to import.", "Empty Thread", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsSaving = true;
        StatusText = "Saving thread to offline archive...";

        try
        {
            string threadId = Guid.NewGuid().ToString("N");
            string datePrefix = DateTime.Now.ToString("yyyyMMdd");
            string threadFolder = Path.Combine(_mediaBaseDir, $"{datePrefix}_thread_{threadId.Substring(0, 8)}");
            Directory.CreateDirectory(threadFolder);

            // Extract any image URLs in the raw input to download
            var detectedImageUrls = await Task.Run(() => ThreadParserHelper.ExtractImageUrlsFromHtmlOrText(RawInputText));
            List<string> downloadedImages = new();
            if (detectedImageUrls.Count > 0)
            {
                StatusText = $"Downloading {detectedImageUrls.Count} thread image(s)...";
                downloadedImages = await ThreadParserHelper.DownloadMediaUrlsAsync(detectedImageUrls, threadFolder);
            }

            var postsToInsert = new List<Post>();
            DateTime baseTime = DateTime.Now.AddMinutes(-validSegments.Count);

            for (int i = 0; i < validSegments.Count; i++)
            {
                var seg = validSegments[i];
                string postId = Guid.NewGuid().ToString("N");
                var post = new Post
                {
                    Id = postId,
                    AuthorName = string.IsNullOrWhiteSpace(AuthorName) ? "Thread Author" : AuthorName.Trim(),
                    AuthorHandle = string.IsNullOrWhiteSpace(AuthorHandle) ? "@author" : (AuthorHandle.StartsWith('@') ? AuthorHandle.Trim() : "@" + AuthorHandle.Trim()),
                    Content = seg.Content.Trim(),
                    PostDate = baseTime.AddMinutes(i),
                    FolderName = string.IsNullOrWhiteSpace(TargetFolder) ? "Threads" : TargetFolder.Trim(),
                    Tags = TagsText?.Trim() ?? string.Empty,
                    FolderPath = threadFolder,
                    CreatedAt = DateTime.Now,
                    ThreadId = threadId,
                    ThreadIndex = i + 1
                };

                // Associate images if this is the first tweet or has matching media
                if (i == 0 && downloadedImages.Count > 0)
                {
                    foreach (var imgPath in downloadedImages)
                    {
                        post.MediaItems.Add(new MediaItem
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            PostId = postId,
                            FilePath = imgPath,
                            MediaType = "Image",
                            CreatedAt = DateTime.Now
                        });
                    }
                }

                postsToInsert.Add(post);
            }

            // High-performance batch insert in single atomic ESE transaction
            StatusText = $"Writing {postsToInsert.Count} tweets into ESE Database...";
            await _dbService.InsertPostsBatchAsync(postsToInsert);

            ImportedPosts = postsToInsert;
            StatusText = $"Successfully imported {postsToInsert.Count} tweets!";
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save thread:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}

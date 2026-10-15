using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;
using LocalSocialArchive.Services;

namespace LocalSocialArchive.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IEseDatabaseService _dbService;
    private readonly IClipboardService _clipboardService;
    private readonly ISecurityService _securityService;
    private readonly ISettingsService _settingsService;
    private readonly ITweetImporterService _tweetImporterService;
    private readonly string _configFilePath;

    [ObservableProperty]
    private string _defaultFolder = "Coding";

    [ObservableProperty]
    private bool _autoExtractHashtags = true;

    partial void OnIsGridViewChanged(bool value)
    {
        _settingsService.IsGridView = value;
        _settingsService.Save();
    }

    partial void OnIsInspectorVisibleChanged(bool value)
    {
        _settingsService.IsInspectorVisible = value;
        _settingsService.Save();
    }

    partial void OnIsTagsVisibleChanged(bool value)
    {
        _settingsService.IsTagsVisible = value;
        _settingsService.Save();
    }

    partial void OnIsSidebarExpandedChanged(bool value)
    {
        _settingsService.IsSidebarExpanded = value;
        _settingsService.Save();
    }

    partial void OnDefaultFolderChanged(string value)
    {
        _settingsService.DefaultFolder = value;
        _settingsService.Save();
    }

    partial void OnAutoExtractHashtagsChanged(bool value)
    {
        _settingsService.AutoExtractHashtags = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridCardMinHeight))]
    [NotifyPropertyChangedFor(nameof(GridMediaHeight))]
    [NotifyPropertyChangedFor(nameof(GridColumnLabel))]
    private double _gridCardWidth = 320;

    public double GridCardMinHeight => Math.Round(GridCardWidth * 0.90);
    public double GridMediaHeight => Math.Round(Math.Max(100, GridCardWidth * 0.58));

    public string GridColumnLabel => GridCardWidth switch
    {
        <= 215 => "5+ Cols",
        <= 275 => "4 Cols",
        <= 365 => "3 Cols",
        <= 455 => "2 Cols",
        _ => "1-2 Cols"
    };

    partial void OnGridCardWidthChanged(double value)
    {
        _settingsService.GridCardWidth = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppScalePercentageText))]
    private double _appScale = 1.25;

    public string AppScalePercentageText => $"{(int)Math.Round(AppScale * 100)}%";

    partial void OnAppScaleChanged(double value)
    {
        _settingsService.AppScale = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private ObservableCollection<Post> _posts = new();

    partial void OnPostsChanged(ObservableCollection<Post> value)
    {
        OnPropertyChanged(nameof(QuickFilterAllCount));
        OnPropertyChanged(nameof(QuickFilterThreadsCount));
        OnPropertyChanged(nameof(QuickFilterMediaCount));
        OnPropertyChanged(nameof(QuickFilterNotesCount));
    }

    // ========== QUICK FILTER CHIPS STATE ==========
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQuickFilterAll))]
    [NotifyPropertyChangedFor(nameof(IsQuickFilterThreads))]
    [NotifyPropertyChangedFor(nameof(IsQuickFilterMedia))]
    [NotifyPropertyChangedFor(nameof(IsQuickFilterNotes))]
    private string _activeQuickFilter = "All";

    public bool IsQuickFilterAll => string.Equals(ActiveQuickFilter, "All", StringComparison.OrdinalIgnoreCase);
    public bool IsQuickFilterThreads => string.Equals(ActiveQuickFilter, "Threads", StringComparison.OrdinalIgnoreCase);
    public bool IsQuickFilterMedia => string.Equals(ActiveQuickFilter, "Media", StringComparison.OrdinalIgnoreCase);
    public bool IsQuickFilterNotes => string.Equals(ActiveQuickFilter, "Notes", StringComparison.OrdinalIgnoreCase);
    public bool IsQuickFilterComments => string.Equals(ActiveQuickFilter, "Comments", StringComparison.OrdinalIgnoreCase);

    public int QuickFilterAllCount => Posts.Count;
    public int QuickFilterThreadsCount => Posts.Count(p => p.IsPartOfThread);
    public int QuickFilterMediaCount => Posts.Count(p => p.HasMedia);
    public int QuickFilterNotesCount => Posts.Count(p => p.HasComment);
    public int QuickFilterCommentsCount => Posts.Count(p => p.HasComments);

    [RelayCommand]
    public void SetQuickFilter(string filter)
    {
        if (string.Equals(ActiveQuickFilter, filter, StringComparison.OrdinalIgnoreCase) && filter != "All")
        {
            ActiveQuickFilter = "All";
        }
        else
        {
            ActiveQuickFilter = filter ?? "All";
        }
        ApplyFilter();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyFeed))]
    private ObservableCollection<Post> _filteredPosts = new();

    public bool IsEmptyFeed => FilteredPosts == null || FilteredPosts.Count == 0;

    partial void OnFilteredPostsChanged(ObservableCollection<Post>? oldValue, ObservableCollection<Post> newValue)
    {
        if (oldValue != null)
        {
            oldValue.CollectionChanged -= FilteredPosts_CollectionChanged;
        }
        if (newValue != null)
        {
            newValue.CollectionChanged += FilteredPosts_CollectionChanged;
        }
        OnPropertyChanged(nameof(IsEmptyFeed));
    }

    private void FilteredPosts_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmptyFeed));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPostStoryParts))]
    private Post? _selectedPost;

    // ========== MULTI-SELECTION STATE ==========
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPostsCount))]
    [NotifyPropertyChangedFor(nameof(HasMultipleSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionStatusText))]
    [NotifyPropertyChangedFor(nameof(DeleteSingleMenuHeader))]
    private ObservableCollection<Post> _selectedPosts = new();

    public int SelectedPostsCount => SelectedPosts.Count;
    public bool HasMultipleSelection => SelectedPosts.Count > 1;
    public string SelectionStatusText => SelectedPosts.Count switch
    {
        0 => string.Empty,
        1 => "1 post selected",
        _ => $"{SelectedPosts.Count} posts selected"
    };
    public string DeleteSingleMenuHeader => HasMultipleSelection ? "🗑️ Delete Only This Post" : "🗑️ Delete Entry";

    public event Action? RequestClearSelection;
    public event Action? RequestSelectAll;

    public void UpdateSelectedPosts(IEnumerable<Post> selected)
    {
        SelectedPosts.Clear();
        foreach (var p in selected)
        {
            SelectedPosts.Add(p);
        }
        if (SelectedPosts.Count == 1)
        {
            SelectedPost = SelectedPosts[0];
        }
        else if (SelectedPosts.Count > 1 && (SelectedPost == null || !SelectedPosts.Contains(SelectedPost)))
        {
            SelectedPost = SelectedPosts.LastOrDefault();
        }
        OnPropertyChanged(nameof(SelectedPostsCount));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectionStatusText));
        OnPropertyChanged(nameof(DeleteSingleMenuHeader));
    }

    [RelayCommand]
    public void ClearSelection()
    {
        SelectedPosts.Clear();
        RequestClearSelection?.Invoke();
        OnPropertyChanged(nameof(SelectedPostsCount));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectionStatusText));
        OnPropertyChanged(nameof(DeleteSingleMenuHeader));
    }

    [RelayCommand]
    public void SelectAllPosts()
    {
        RequestSelectAll?.Invoke();
    }

    // ========== TOP-LEVEL COLLECTIONS (Each contains a group of Folders) ==========
    [ObservableProperty]
    private ObservableCollection<CollectionGroup> _collections = new();

    [ObservableProperty]
    private ObservableCollection<CollectionGroup> _filteredCollections = new();

    [ObservableProperty]
    private ObservableCollection<FolderCategory> _matchedFolders = new();

    [ObservableProperty]
    private bool _hasMatchedFolders;

    [ObservableProperty]
    private CollectionGroup? _selectedCollection;

    // ========== WINDOWS 11 FILE EXPLORER VIEW (Minimal Collections & Folders Hub) ==========
    [ObservableProperty]
    private ObservableCollection<FolderCategory> _explorerDisplayFolders = new();

    [ObservableProperty]
    private string _selectedExplorerCollection = "All Folders";

    [ObservableProperty]
    private ObservableCollection<ExplorerPillItem> _explorerCollectionPills = new();

    [ObservableProperty]
    private string _explorerSearchText = string.Empty;

    [ObservableProperty]
    private bool _isExplorerViewDetails;

    [ObservableProperty]
    private string _explorerSortMode = "Name"; // "Name", "Count", "Recent"

    [ObservableProperty]
    private FolderCategory? _selectedExplorerFolder;

    // ========== FLAT FOLDERS (For quick lookup, filter dropdowns, and "All Posts") ==========
    [ObservableProperty]
    private ObservableCollection<FolderCategory> _folders = new();

    public IEnumerable<FolderCategory> DestinationFolders => Folders.Where(f => !string.Equals(f.Name, "All Posts", StringComparison.OrdinalIgnoreCase));

    [ObservableProperty]
    private FolderCategory? _selectedFolder;

    [ObservableProperty]
    private ObservableCollection<TagCategory> _tags = new();

    [ObservableProperty]
    private TagCategory? _selectedTag;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _databaseSizeText = "DB Size: 0.0 MB";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _selectedFilterDropdownText = "Filter: All Posts";

    [ObservableProperty]
    private ObservableCollection<string> _filterDropdownOptions = new();

    // ========== VIEW MODE (List vs Grid) ==========
    [ObservableProperty]
    private bool _isGridView;

    // ========== PANEL VISIBILITY (Sidebar, Inspector, Tags & Collections Hub) ==========
    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    [ObservableProperty]
    private string _activeRailView = "Home"; // "Home", "Feed", "Folders"

    [ObservableProperty]
    private bool _isInspectorVisible = true;

    [ObservableProperty]
    private bool _isTagsVisible = true;

    [ObservableProperty]
    private bool _isFoldersViewActive;

    // ========== WINDOWS 11 FLUENT DASHBOARD & USER INFO ==========
    [ObservableProperty]
    private string _currentUserDisplayName = Environment.UserName;

    [ObservableProperty]
    private string _machineDeviceName = Environment.MachineName;

    [ObservableProperty]
    private bool _isHomeDashboardActive = true;

    [ObservableProperty]
    private string _selectedThemeName = "Windows Dark";

    partial void OnSelectedThemeNameChanged(string value)
    {
        _settingsService.ThemeName = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private ObservableCollection<FolderCategory> _topDashboardFolders = new();

    // ========== THEATER MODE (Twitter-style Split Screen: Left Media / Right Text) ==========
    [ObservableProperty]
    private bool _isTheaterModeOpen;

    [ObservableProperty]
    private Post? _theaterPost;

    [ObservableProperty]
    private MediaItem? _currentTheaterMedia;

    [ObservableProperty]
    private int _currentTheaterMediaIndex;

    [ObservableProperty]
    private string _theaterMediaCounter = string.Empty;

    [ObservableProperty]
    private bool _hasMultipleTheaterMedia;

    [ObservableProperty]
    private bool _isTheaterMediaVideo;

    public MainViewModel(
        IEseDatabaseService dbService, 
        IClipboardService clipboardService, 
        ISecurityService securityService, 
        ISettingsService? settingsService = null,
        ITweetImporterService? tweetImporterService = null)
    {
        _dbService = dbService;
        _clipboardService = clipboardService;
        _securityService = securityService;
        _settingsService = settingsService ?? new SettingsService();
        _tweetImporterService = tweetImporterService ?? new TweetImporterService(_dbService);

        _isGridView = _settingsService.IsGridView;
        _isInspectorVisible = _settingsService.IsInspectorVisible;
        _isTagsVisible = _settingsService.IsTagsVisible;
        _isSidebarExpanded = _settingsService.IsSidebarExpanded;
        _defaultFolder = !string.IsNullOrWhiteSpace(_settingsService.DefaultFolder) ? _settingsService.DefaultFolder : "Coding";
        _autoExtractHashtags = _settingsService.AutoExtractHashtags;
        _gridCardWidth = _settingsService.GridCardWidth >= 180 && _settingsService.GridCardWidth <= 600 ? _settingsService.GridCardWidth : 320;
        _appScale = _settingsService.AppScale >= 0.75 && _settingsService.AppScale <= 2.0 ? _settingsService.AppScale : 1.25;
        _selectedThemeName = !string.IsNullOrWhiteSpace(_settingsService.ThemeName) ? _settingsService.ThemeName : "Windows Dark";

        _configFilePath = LocalSocialArchive.Helpers.StoragePathHelper.CollectionsConfigPath;

        _filteredPosts.CollectionChanged += FilteredPosts_CollectionChanged;

        InitializeHierarchy();
    }

    private void InitializeHierarchy()
    {
        LoadHierarchyConfig();
        RebuildFlatFolders();
    }

    private void LoadHierarchyConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                var data = JsonSerializer.Deserialize<List<CollectionDto>>(json);
                if (data != null && data.Count > 0)
                {
                    var colList = new ObservableCollection<CollectionGroup>();
                    foreach (var cDto in data)
                    {
                        var col = new CollectionGroup
                        {
                            Name = cDto.Name,
                            IconGlyph = cDto.IconGlyph ?? "🗂️",
                            Description = cDto.Description ?? string.Empty,
                            AccentColor = cDto.AccentColor ?? "#1D9BF0",
                            IsExpanded = true,
                            IsDeletable = cDto.IsDeletable
                        };

                        if (cDto.Folders != null)
                        {
                            foreach (var fDto in cDto.Folders)
                            {
                                col.Folders.Add(new FolderCategory
                                {
                                    Name = fDto.Name,
                                    CollectionName = col.Name,
                                    IconGlyph = fDto.IconGlyph ?? "📁",
                                    Description = fDto.Description ?? "Offline categorized archive",
                                    AccentColor = fDto.AccentColor ?? col.AccentColor
                                });
                            }
                        }
                        colList.Add(col);
                    }
                    Collections = colList;
                    return;
                }
            }
        }
        catch { }

        // Default Collections (Collections of Folders)
        var defaultCollections = new ObservableCollection<CollectionGroup>();

        // 1. Engineering & Tech
        var techCollection = new CollectionGroup
        {
            Name = "Engineering & Tech",
            IconGlyph = "💻",
            AccentColor = "#1D9BF0",
            Description = "Software engineering, C# & tech tips",
            IsExpanded = true,
            IsDeletable = false
        };
        techCollection.Folders.Add(new FolderCategory
        {
            Name = "Coding",
            CollectionName = "Engineering & Tech",
            IconGlyph = "💻",
            AccentColor = "#1D9BF0",
            Description = "Software engineering, C# & tech tips"
        });
        defaultCollections.Add(techCollection);

        // 2. Enterprise & SAP
        var sapCollection = new CollectionGroup
        {
            Name = "Enterprise & SAP",
            IconGlyph = "💼",
            AccentColor = "#10B981",
            Description = "Enterprise SAP, ABAP & CAP projects",
            IsExpanded = true,
            IsDeletable = true
        };
        sapCollection.Folders.Add(new FolderCategory
        {
            Name = "SAP Projects",
            CollectionName = "Enterprise & SAP",
            IconGlyph = "💼",
            AccentColor = "#10B981",
            Description = "Enterprise SAP, ABAP & CAP projects"
        });
        defaultCollections.Add(sapCollection);

        // 3. Personal Vault
        var personalCollection = new CollectionGroup
        {
            Name = "Personal Vault",
            IconGlyph = "🌟",
            AccentColor = "#8B5CF6",
            Description = "Viral social humor, bookmarks & references",
            IsExpanded = true,
            IsDeletable = true
        };
        personalCollection.Folders.Add(new FolderCategory
        {
            Name = "Memes",
            CollectionName = "Personal Vault",
            IconGlyph = "🎭",
            AccentColor = "#F59E0B",
            Description = "Viral social humor & developer memes"
        });
        personalCollection.Folders.Add(new FolderCategory
        {
            Name = "Reference",
            CollectionName = "Personal Vault",
            IconGlyph = "📚",
            AccentColor = "#8B5CF6",
            Description = "Tutorials, cheat sheets & bookmarks"
        });
        defaultCollections.Add(personalCollection);

        Collections = defaultCollections;
        SaveHierarchyConfig();
    }

    private void SaveHierarchyConfig()
    {
        try
        {
            var list = new List<CollectionDto>();
            foreach (var col in Collections)
            {
                var cDto = new CollectionDto
                {
                    Name = col.Name,
                    IconGlyph = col.IconGlyph,
                    Description = col.Description,
                    AccentColor = col.AccentColor,
                    IsDeletable = col.IsDeletable,
                    Folders = col.Folders.Select(f => new FolderDto
                    {
                        Name = f.Name,
                        IconGlyph = f.IconGlyph,
                        Description = f.Description,
                        AccentColor = f.AccentColor
                    }).ToList()
                };
                list.Add(cDto);
            }

            string? dir = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch { }
    }

    private void RebuildFlatFolders()
    {
        var flat = new ObservableCollection<FolderCategory>
        {
            new FolderCategory
            {
                Name = "All Posts",
                CollectionName = "All",
                Count = Posts.Count,
                IconGlyph = "🌐",
                AccentColor = "#38BDF8",
                Description = "Complete offline social database",
                IsSelected = SelectedFolder == null || SelectedFolder.Name == "All Posts"
            }
        };

        foreach (var col in Collections)
        {
            foreach (var f in col.Folders)
            {
                f.CollectionName = col.Name;
                if (SelectedFolder != null && string.Equals(f.Name, SelectedFolder.Name, StringComparison.OrdinalIgnoreCase))
                {
                    f.IsSelected = true;
                }
                flat.Add(f);
            }
        }

        Folders = flat;
        OnPropertyChanged(nameof(DestinationFolders));

        TopDashboardFolders = new ObservableCollection<FolderCategory>(
            flat.Where(f => !string.Equals(f.Name, "All Posts", StringComparison.OrdinalIgnoreCase)).Take(4)
        );

        if (SelectedFolder == null || !Folders.Any(f => f.Name == SelectedFolder.Name))
        {
            SelectedFolder = Folders[0];
            SelectedFolder.IsSelected = true;
        }

        UpdateFilterDropdownOptions();
        RefreshFilteredCollections();
        UpdateExplorerDisplayFolders();
    }

    private void UpdateFilterDropdownOptions()
    {
        var options = new ObservableCollection<string>();
        foreach (var f in Folders)
        {
            options.Add($"Filter: {f.Name}");
        }
        FilterDropdownOptions = options;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Initializing Windows ESE Database...";

        try
        {
            await _dbService.InitializeAsync();
            await _dbService.SeedSampleDataIfEmptyAsync();
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"DB Error: {ex.Message}";
            MessageBox.Show($"Failed to initialize ESE Database:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshDataAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _dbService.GetAllPostsAsync();
            Posts = new ObservableCollection<Post>(list);

            // Dynamically discover any folder names in database that aren't yet in our hierarchy
            bool hierarchyChanged = false;
            foreach (var post in list)
            {
                if (!string.IsNullOrWhiteSpace(post.FolderName))
                {
                    bool exists = Collections.Any(c => c.Folders.Any(f => string.Equals(f.Name, post.FolderName, StringComparison.OrdinalIgnoreCase)));
                    if (!exists && !string.Equals(post.FolderName, "All Posts", StringComparison.OrdinalIgnoreCase))
                    {
                        var targetCol = Collections.FirstOrDefault(c => c.Name == "Engineering & Tech") ?? Collections.FirstOrDefault();
                        if (targetCol == null)
                        {
                            targetCol = new CollectionGroup { Name = "General Archive", IconGlyph = "🗂️", AccentColor = "#1D9BF0" };
                            Collections.Add(targetCol);
                        }

                        targetCol.Folders.Add(new FolderCategory
                        {
                            Name = post.FolderName,
                            CollectionName = targetCol.Name,
                            IconGlyph = "📁",
                            AccentColor = targetCol.AccentColor
                        });
                        hierarchyChanged = true;
                    }
                }
            }

            if (hierarchyChanged)
            {
                RebuildFlatFolders();
                SaveHierarchyConfig();
            }

            await UpdateCountsAndSizeAsync();
            ApplyFilter();

            if (SelectedPost == null || !Posts.Any(p => p.Id == SelectedPost.Id))
            {
                SelectedPost = FilteredPosts.FirstOrDefault();
            }

            StatusMessage = $"Loaded {Posts.Count} archived posts across {Collections.Count} collections.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task UpdateCountsAndSizeAsync()
    {
        long bytes = await _dbService.GetDatabaseSizeBytesAsync();
        if (bytes < 1024 * 1024)
        {
            DatabaseSizeText = $"DB Size: {bytes / 1024.0:F1} KB";
        }
        else
        {
            DatabaseSizeText = $"DB Size: {bytes / (1024.0 * 1024.0):F1} MB";
        }

        // Fast RAM-based count aggregation from in-memory Posts (zero disk I/O)
        var folderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["All Posts"] = Posts.Count,
            ["Coding"] = 0,
            ["SAP Projects"] = 0,
            ["Memes"] = 0,
            ["Reference"] = 0
        };

        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var postsByFolder = new Dictionary<string, List<Post>>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Posts)
        {
            var folder = string.IsNullOrWhiteSpace(p.FolderName) ? "Coding" : p.FolderName;
            if (folderCounts.ContainsKey(folder))
                folderCounts[folder]++;
            else
                folderCounts[folder] = 1;

            if (!postsByFolder.TryGetValue(folder, out var fList))
            {
                fList = new List<Post>();
                postsByFolder[folder] = fList;
            }
            fList.Add(p);

            foreach (var tag in p.TagList)
            {
                if (tagCounts.ContainsKey(tag))
                    tagCounts[tag]++;
                else
                    tagCounts[tag] = 1;
            }
        }

        foreach (var col in Collections)
        {
            foreach (var folder in col.Folders)
            {
                if (folderCounts.TryGetValue(folder.Name, out int cnt))
                {
                    folder.Count = cnt;
                }
                else
                {
                    folder.Count = 0;
                }

                // Extract up to 3 media preview paths for this folder via O(1) dictionary lookup
                postsByFolder.TryGetValue(folder.Name, out var folderPosts);
                var thumbs = new ObservableCollection<string>();
                if (folderPosts != null)
                {
                    foreach (var p in folderPosts)
                    {
                        foreach (var media in p.MediaItems)
                        {
                            var thumbPath = media.DisplayThumbnail ?? media.FilePath;
                            if (!string.IsNullOrWhiteSpace(thumbPath) && !thumbs.Contains(thumbPath))
                            {
                                thumbs.Add(thumbPath);
                                if (thumbs.Count >= 3) break;
                            }
                        }
                        if (thumbs.Count >= 3) break;
                    }
                }
                folder.RecentThumbnails = thumbs;

                if (folderPosts != null && folderPosts.Count > 0)
                {
                    var latest = folderPosts.OrderByDescending(p => p.PostDate).FirstOrDefault();
                    if (latest != null)
                    {
                        folder.LastUpdatedText = $"Updated {latest.RelativeTime}";
                    }
                }
                else
                {
                    folder.LastUpdatedText = "0 items archived";
                }
            }

            col.NotifyHierarchyChanged();
        }

        // Update "All Posts" folder count
        var allFolder = Folders.FirstOrDefault(f => f.Name == "All Posts");
        if (allFolder != null)
        {
            allFolder.Count = Posts.Count;
            allFolder.LastUpdatedText = Posts.Count > 0 ? $"Active ({Posts.Count} total)" : "0 items";
        }

        var tagList = new ObservableCollection<TagCategory>();
        foreach (var kvp in tagCounts.OrderByDescending(k => k.Value))
        {
            tagList.Add(new TagCategory
            {
                Name = kvp.Key,
                Count = kvp.Value,
                IsSelected = SelectedTag != null && string.Equals(SelectedTag.Name, kvp.Key, StringComparison.OrdinalIgnoreCase)
            });
        }
        Tags = tagList;
        UpdateExplorerDisplayFolders();
    }

    [RelayCommand]
    public async Task PasteFromClipboardAsync()
    {
        StatusMessage = "Processing clipboard contents...";
        try
        {
            string defaultTarget = !string.IsNullOrWhiteSpace(DefaultFolder) ? DefaultFolder : "Coding";
            string targetFolder = (SelectedFolder != null && SelectedFolder.Name != "All Posts") ? SelectedFolder.Name : defaultTarget;
            var post = await _clipboardService.ProcessClipboardAsync(targetFolder, AutoExtractHashtags);

            if (post != null)
            {
                Posts.Insert(0, post);
                await UpdateCountsAndSizeAsync();
                ApplyFilter();
                SelectedPost = post;
                StatusMessage = $"Archived new post by {post.AuthorName} successfully into '{targetFolder}'!";
            }
            else
            {
                StatusMessage = "Clipboard is empty or contains unsupported data.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Clipboard Error: {ex.Message}";
            MessageBox.Show($"Failed to ingest clipboard:\n{ex.Message}", "Clipboard Ingestion Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    public void SelectFolder(FolderCategory? folder)
    {
        if (folder == null) return;

        IsHomeDashboardActive = false;
        IsFoldersViewActive = false;
        ActiveRailView = "Feed";
        foreach (var f in Folders) f.IsSelected = false;
        folder.IsSelected = true;
        SelectedFolder = folder;

        if (SelectedTag != null)
        {
            SelectedTag.IsSelected = false;
            SelectedTag = null;
        }

        SelectedFilterDropdownText = folder.Name == "All Posts" ? "Filter: All Posts" : $"Filter: {folder.Name}";
        ApplyFilter();
    }

    [RelayCommand]
    public void SelectTag(TagCategory? tag)
    {
        if (tag == null) return;

        bool wasSelected = tag.IsSelected;
        foreach (var t in Tags) t.IsSelected = false;

        if (wasSelected)
        {
            SelectedTag = null;
        }
        else
        {
            tag.IsSelected = true;
            SelectedTag = tag;
            IsHomeDashboardActive = false;
            IsFoldersViewActive = false;
            ActiveRailView = "Feed";
        }

        ApplyFilter();
    }

    [RelayCommand]
    public void FilterByTagString(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return;
        var cleanTag = tagName.TrimStart('#').ToLowerInvariant();

        var matchedTag = Tags.FirstOrDefault(t => string.Equals(t.Name, cleanTag, StringComparison.OrdinalIgnoreCase));
        if (matchedTag != null)
        {
            SelectTag(matchedTag);
        }
        else
        {
            SearchQuery = $"#{cleanTag}";
        }
    }

    [RelayCommand]
    public void SelectFilterDropdown(string? option)
    {
        if (string.IsNullOrWhiteSpace(option)) return;
        SelectedFilterDropdownText = option;

        string folderName = option.Replace("Filter: ", "").Trim();
        var folder = Folders.FirstOrDefault(f => string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase));
        if (folder != null)
        {
            SelectFolder(folder);
        }
        else
        {
            ApplyFilter();
        }
    }

    private System.Threading.CancellationTokenSource? _searchDebounceCts;

    partial void OnSearchQueryChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (IsHomeDashboardActive || IsFoldersViewActive)
            {
                IsHomeDashboardActive = false;
                IsFoldersViewActive = false;
                ActiveRailView = "Feed";
            }
        }

        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new System.Threading.CancellationTokenSource();
        var token = _searchDebounceCts.Token;

        Task.Delay(160, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Application.Current?.Dispatcher.InvokeAsync(ApplyFilter);
            }
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    public void ClearSearch()
    {
        _searchDebounceCts?.Cancel();
        SearchQuery = string.Empty;
        ApplyFilter();
    }

    private void RefreshFilteredCollections()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredCollections = new ObservableCollection<CollectionGroup>(Collections);
            MatchedFolders = new ObservableCollection<FolderCategory>();
            HasMatchedFolders = false;
            return;
        }

        string q = SearchQuery.Trim().ToLowerInvariant();
        if (q.StartsWith("folder:", StringComparison.OrdinalIgnoreCase) || q.StartsWith("f:", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Substring(q.IndexOf(':') + 1).Trim();
        }

        var matchedCols = new ObservableCollection<CollectionGroup>();
        var matchingFolders = new List<FolderCategory>();

        foreach (var col in Collections)
        {
            bool colMatches = (!string.IsNullOrWhiteSpace(col.Name) && col.Name.ToLowerInvariant().Contains(q)) ||
                              (!string.IsNullOrWhiteSpace(col.Description) && col.Description.ToLowerInvariant().Contains(q));

            var matchingFoldersInCol = col.Folders.Where(f =>
                (!string.IsNullOrWhiteSpace(f.Name) && f.Name.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrWhiteSpace(f.Description) && f.Description.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrWhiteSpace(f.CollectionName) && f.CollectionName.ToLowerInvariant().Contains(q))
            ).ToList();

            foreach (var mf in matchingFoldersInCol)
            {
                if (!matchingFolders.Any(existing => string.Equals(existing.Name, mf.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    matchingFolders.Add(mf);
                }
            }

            if (colMatches)
            {
                matchedCols.Add(new CollectionGroup
                {
                    Name = col.Name,
                    IconGlyph = col.IconGlyph,
                    Description = col.Description,
                    AccentColor = col.AccentColor,
                    IsExpanded = true,
                    IsDeletable = col.IsDeletable,
                    Folders = new ObservableCollection<FolderCategory>(col.Folders)
                });
            }
            else if (matchingFoldersInCol.Count > 0)
            {
                matchedCols.Add(new CollectionGroup
                {
                    Name = col.Name,
                    IconGlyph = col.IconGlyph,
                    Description = col.Description,
                    AccentColor = col.AccentColor,
                    IsExpanded = true,
                    IsDeletable = col.IsDeletable,
                    Folders = new ObservableCollection<FolderCategory>(matchingFoldersInCol)
                });
            }
        }

        FilteredCollections = matchedCols;
        MatchedFolders = new ObservableCollection<FolderCategory>(matchingFolders);
        HasMatchedFolders = MatchedFolders.Count > 0;
    }

    private void ApplyFilter()
    {
        RefreshFilteredCollections();

        IEnumerable<Post> query = Posts;

        // Apply Quick Filter Chips
        if (string.Equals(ActiveQuickFilter, "Threads", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.IsPartOfThread);
        }
        else if (string.Equals(ActiveQuickFilter, "Media", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.HasMedia);
        }
        else if (string.Equals(ActiveQuickFilter, "Notes", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.HasComment);
        }
        else if (string.Equals(ActiveQuickFilter, "Comments", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(p => p.HasComments);
        }

        string rawSearch = SearchQuery?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(rawSearch))
        {
            var tokens = rawSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var textTokens = new List<string>();

            foreach (var token in tokens)
            {
                if (token.StartsWith("thread:", StringComparison.OrdinalIgnoreCase) || token.StartsWith("story:", StringComparison.OrdinalIgnoreCase))
                {
                    string tid = token.Substring(token.IndexOf(':') + 1);
                    if (!string.IsNullOrEmpty(tid))
                    {
                        query = query.Where(p => string.Equals(p.ThreadId, tid, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.ThreadIndex);
                    }
                }
                else if (token.StartsWith("folder:", StringComparison.OrdinalIgnoreCase) || token.StartsWith("f:", StringComparison.OrdinalIgnoreCase))
                {
                    string f = token.Substring(token.IndexOf(':') + 1).ToLowerInvariant();
                    if (!string.IsNullOrEmpty(f))
                    {
                        query = query.Where(p => !string.IsNullOrEmpty(p.FolderName) && p.FolderName.ToLowerInvariant().Contains(f));
                    }
                }
                else if (token.StartsWith("from:", StringComparison.OrdinalIgnoreCase) || token.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
                {
                    string author = token.Substring(token.IndexOf(':') + 1).TrimStart('@').ToLowerInvariant();
                    if (!string.IsNullOrEmpty(author))
                    {
                        query = query.Where(p => 
                            (!string.IsNullOrEmpty(p.AuthorHandle) && p.AuthorHandle.ToLowerInvariant().Contains(author)) ||
                            (!string.IsNullOrEmpty(p.AuthorName) && p.AuthorName.ToLowerInvariant().Contains(author)));
                    }
                }
                else if (token.StartsWith('@') && token.Length > 1)
                {
                    string handle = token.TrimStart('@').ToLowerInvariant();
                    query = query.Where(p => 
                        (!string.IsNullOrEmpty(p.AuthorHandle) && p.AuthorHandle.ToLowerInvariant().Contains(handle)) ||
                        (!string.IsNullOrEmpty(p.AuthorName) && p.AuthorName.ToLowerInvariant().Contains(handle)));
                }
                else if (token.Equals("has:media", StringComparison.OrdinalIgnoreCase) || token.Equals("has:image", StringComparison.OrdinalIgnoreCase) || token.Equals("has:images", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(p => p.HasMedia);
                }
                else if (token.Equals("has:video", StringComparison.OrdinalIgnoreCase) || token.Equals("has:videos", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(p => p.MediaItems.Any(m => m.IsVideo));
                }
                else if (token.Equals("has:comment", StringComparison.OrdinalIgnoreCase) || token.Equals("has:note", StringComparison.OrdinalIgnoreCase) || token.Equals("has:notes", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(p => p.HasComment);
                }
                else if (token.Equals("has:comments", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(p => p.HasComments);
                }
                else if (token.Equals("is:thread", StringComparison.OrdinalIgnoreCase) || token.Equals("is:reply", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(p => p.IsPartOfThread);
                }
                else if (token.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
                {
                    string t = token.Substring(4).TrimStart('#').ToLowerInvariant();
                    if (!string.IsNullOrEmpty(t))
                    {
                        query = query.Where(p => p.TagList.Any(tag => tag.ToLowerInvariant().Contains(t)));
                    }
                }
                else if (token.StartsWith('#') && token.Length > 1)
                {
                    string t = token.TrimStart('#').ToLowerInvariant();
                    query = query.Where(p => p.TagList.Any(tag => tag.ToLowerInvariant().Contains(t)));
                }
                else
                {
                    textTokens.Add(token.ToLowerInvariant());
                }
            }

            if (textTokens.Count > 0)
            {
                string textQuery = string.Join(" ", textTokens);
                if (SelectedFolder != null && SelectedFolder.Name != "All Posts")
                {
                    var inFolderQuery = query.Where(p => string.Equals(p.FolderName, SelectedFolder.Name, StringComparison.OrdinalIgnoreCase));
                    var inFolderFiltered = inFolderQuery.Where(p =>
                        (!string.IsNullOrEmpty(p.Content) && p.Content.ToLowerInvariant().Contains(textQuery)) ||
                        (!string.IsNullOrEmpty(p.AuthorName) && p.AuthorName.ToLowerInvariant().Contains(textQuery)) ||
                        (!string.IsNullOrEmpty(p.AuthorHandle) && p.AuthorHandle.ToLowerInvariant().Contains(textQuery)) ||
                        (!string.IsNullOrEmpty(p.FolderName) && p.FolderName.ToLowerInvariant().Contains(textQuery)) ||
                        (p.Comment != null && p.Comment.ToLowerInvariant().Contains(textQuery)) ||
                        p.TagList.Any(t => t.ToLowerInvariant().Contains(textQuery))
                    ).ToList();

                    if (inFolderFiltered.Count > 0)
                    {
                        query = inFolderFiltered;
                    }
                    else
                    {
                        query = query.Where(p =>
                            (!string.IsNullOrEmpty(p.Content) && p.Content.ToLowerInvariant().Contains(textQuery)) ||
                            (!string.IsNullOrEmpty(p.AuthorName) && p.AuthorName.ToLowerInvariant().Contains(textQuery)) ||
                            (!string.IsNullOrEmpty(p.AuthorHandle) && p.AuthorHandle.ToLowerInvariant().Contains(textQuery)) ||
                            (!string.IsNullOrEmpty(p.FolderName) && p.FolderName.ToLowerInvariant().Contains(textQuery)) ||
                            (p.Comment != null && p.Comment.ToLowerInvariant().Contains(textQuery)) ||
                            p.TagList.Any(t => t.ToLowerInvariant().Contains(textQuery))
                        );
                    }
                }
                else
                {
                    query = query.Where(p =>
                        (!string.IsNullOrEmpty(p.Content) && p.Content.ToLowerInvariant().Contains(textQuery)) ||
                        (!string.IsNullOrEmpty(p.AuthorName) && p.AuthorName.ToLowerInvariant().Contains(textQuery)) ||
                        (!string.IsNullOrEmpty(p.AuthorHandle) && p.AuthorHandle.ToLowerInvariant().Contains(textQuery)) ||
                        (!string.IsNullOrEmpty(p.FolderName) && p.FolderName.ToLowerInvariant().Contains(textQuery)) ||
                        (p.Comment != null && p.Comment.ToLowerInvariant().Contains(textQuery)) ||
                        p.Comments.Any(c => c.Content.ToLowerInvariant().Contains(textQuery) || c.AuthorHandle.ToLowerInvariant().Contains(textQuery) || c.AuthorName.ToLowerInvariant().Contains(textQuery)) ||
                        p.TagList.Any(t => t.ToLowerInvariant().Contains(textQuery))
                    );
                }
            }
        }
        else
        {
            if (SelectedFolder != null && SelectedFolder.Name != "All Posts")
            {
                query = query.Where(p => string.Equals(p.FolderName, SelectedFolder.Name, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (SelectedTag != null)
        {
            string tagName = SelectedTag.Name.TrimStart('#').ToLowerInvariant();
            query = query.Where(p => p.TagList.Any(t => string.Equals(t, tagName, StringComparison.OrdinalIgnoreCase)));
        }

        var newList = query.ToList();
        if (FilteredPosts.Count != newList.Count || !FilteredPosts.SequenceEqual(newList))
        {
            FilteredPosts = new ObservableCollection<Post>(newList);
        }

        if (SelectedPost == null || !FilteredPosts.Contains(SelectedPost))
        {
            SelectedPost = FilteredPosts.FirstOrDefault();
        }
    }

    // ========== COLLECTION MANAGEMENT (Collections containing Folders) ==========
    [RelayCommand]
    public void CreateNewCollection()
    {
        var addVm = new AddCollectionViewModel();
        var addWin = new Views.AddCollectionWindow(addVm)
        {
            Owner = Application.Current.MainWindow
        };

        if (addWin.ShowDialog() == true && !string.IsNullOrWhiteSpace(addVm.CollectionName))
        {
            string name = addVm.CollectionName.Trim();
            if (Collections.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"Collection '{name}' already exists.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var newCol = new CollectionGroup
            {
                Name = name,
                IconGlyph = addVm.SelectedIcon,
                Description = addVm.Description,
                AccentColor = addVm.SelectedColor,
                IsExpanded = true,
                IsDeletable = true
            };

            Collections.Add(newCol);
            SaveHierarchyConfig();
            StatusMessage = $"Created collection '{name}'.";
        }
    }

    [RelayCommand]
    public void DeleteCollection(CollectionGroup? collection)
    {
        if (collection == null || !collection.IsDeletable) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the collection '{collection.Name}'?\n\nAny folders inside it will be moved to 'Engineering & Tech'.",
            "Delete Collection",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var targetCol = Collections.FirstOrDefault(c => c.Name == "Engineering & Tech") ?? Collections.FirstOrDefault(c => c != collection);
        if (targetCol != null)
        {
            foreach (var folder in collection.Folders.ToList())
            {
                folder.CollectionName = targetCol.Name;
                targetCol.Folders.Add(folder);
            }
        }

        Collections.Remove(collection);
        RebuildFlatFolders();
        SaveHierarchyConfig();
        StatusMessage = $"Deleted collection '{collection.Name}'.";
    }

    [RelayCommand]
    public void ToggleCollectionExpand(CollectionGroup? collection)
    {
        if (collection == null) return;
        collection.IsExpanded = !collection.IsExpanded;
    }

    // ========== FOLDER MANAGEMENT (Folders containing Posts) ==========
    [RelayCommand]
    public void CreateNewFolder(object? parameter)
    {
        PromptCreateFolder(parameter);
    }

    public string? PromptCreateFolder(object? parameter = null)
    {
        string? targetCollectionName = null;
        if (parameter is CollectionGroup cg)
        {
            targetCollectionName = cg.Name;
        }
        else if (parameter is string colName)
        {
            targetCollectionName = colName;
        }

        var availableColNames = Collections.Select(c => c.Name).ToList();
        var addVm = new AddFolderViewModel(availableColNames, targetCollectionName);
        var addWin = new Views.AddFolderWindow(addVm)
        {
            Owner = Application.Current.MainWindow
        };

        if (addWin.ShowDialog() == true && !string.IsNullOrWhiteSpace(addVm.FolderName))
        {
            string name = addVm.FolderName.Trim();
            if (Folders.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"Folder '{name}' already exists.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return name;
            }

            var parentCol = Collections.FirstOrDefault(c => string.Equals(c.Name, addVm.CollectionName, StringComparison.OrdinalIgnoreCase))
                            ?? Collections.FirstOrDefault();

            if (parentCol == null)
            {
                parentCol = new CollectionGroup { Name = "General Archive", IconGlyph = "🗂️", AccentColor = "#1D9BF0" };
                Collections.Add(parentCol);
            }

            var newFolder = new FolderCategory
            {
                Name = name,
                CollectionName = parentCol.Name,
                Count = 0,
                IconGlyph = addVm.SelectedIcon,
                Description = addVm.Description,
                AccentColor = parentCol.AccentColor
            };

            parentCol.Folders.Add(newFolder);
            parentCol.IsExpanded = true;
            parentCol.NotifyHierarchyChanged();

            RebuildFlatFolders();
            SaveHierarchyConfig();
            SelectFolder(newFolder);
            StatusMessage = $"Created folder '{name}' in collection '{parentCol.Name}'.";
            return name;
        }
        return null;
    }

    [RelayCommand]
    public async Task DeleteFolderAsync(FolderCategory? folder)
    {
        if (folder == null || !folder.IsDeletable) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the folder '{folder.Name}'?\n\nAny posts currently in this folder will be moved to 'Coding'.",
            "Delete Folder",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            // Reassign any posts from deleted folder to 'Coding'
            var postsToUpdate = Posts.Where(p => string.Equals(p.FolderName, folder.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var post in postsToUpdate)
            {
                post.FolderName = "Coding";
                await _dbService.UpdatePostAsync(post);
            }

            foreach (var col in Collections)
            {
                col.Folders.Remove(folder);
                col.NotifyHierarchyChanged();
            }

            RebuildFlatFolders();
            SaveHierarchyConfig();

            // Reselect All Posts or Coding
            SelectFolder(Folders[0]);
            await UpdateCountsAndSizeAsync();
            ApplyFilter();

            StatusMessage = $"Deleted folder '{folder.Name}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Folder Error: {ex.Message}";
            MessageBox.Show($"Failed to delete folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ========== SIDEBAR TOGGLE (VS Code Style Ctrl+B) ==========
    [RelayCommand]
    public void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    // ========== NAVIGATION & HOME ==========
    [RelayCommand]
    public void NavigateHome()
    {
        ActiveRailView = "Home";
        IsHomeDashboardActive = true;
        IsFoldersViewActive = false;
        if (IsTheaterModeOpen)
        {
            CloseTheaterMode();
        }
        StatusMessage = "Home Dashboard — Overview of your local social archive.";
    }

    [RelayCommand]
    public void NavigateFeed()
    {
        ActiveRailView = "Feed";
        IsHomeDashboardActive = false;
        IsFoldersViewActive = false;

        if (Folders.Count > 0)
        {
            foreach (var f in Folders) f.IsSelected = false;
            Folders[0].IsSelected = true;
            SelectedFolder = Folders[0];
            SelectedFilterDropdownText = "Filter: All Posts";
        }

        if (SelectedTag != null)
        {
            SelectedTag.IsSelected = false;
            SelectedTag = null;
        }

        if (!string.IsNullOrEmpty(SearchQuery))
        {
            SearchQuery = string.Empty;
        }

        if (IsTheaterModeOpen)
        {
            CloseTheaterMode();
        }

        ApplyFilter();

        SelectedPost = FilteredPosts.FirstOrDefault();
        StatusMessage = "Showing all archived posts feed.";
    }

    // ========== COLLECTIONS & FOLDERS EXPLORER SECTION ==========
    partial void OnExplorerSearchTextChanged(string value)
    {
        UpdateExplorerDisplayFolders();
    }

    partial void OnSelectedExplorerCollectionChanged(string value)
    {
        UpdateExplorerDisplayFolders();
    }

    public void UpdateExplorerDisplayFolders()
    {
        // 1. Rebuild ExplorerCollectionPills
        var allCount = Collections.SelectMany(c => c.Folders).DistinctBy(f => f.Name).Count();
        var pills = new ObservableCollection<ExplorerPillItem>
        {
            new ExplorerPillItem
            {
                Name = "All Folders",
                DisplayText = $"All Folders ({allCount})",
                IsActive = SelectedExplorerCollection == "All Folders"
            }
        };

        foreach (var col in Collections)
        {
            if (!string.IsNullOrWhiteSpace(col.Name))
            {
                pills.Add(new ExplorerPillItem
                {
                    Name = col.Name,
                    DisplayText = $"{col.Name} ({col.Folders.Count})",
                    IsActive = string.Equals(col.Name, SelectedExplorerCollection, StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        ExplorerCollectionPills = pills;

        if (string.IsNullOrWhiteSpace(SelectedExplorerCollection) || !pills.Any(p => p.Name == SelectedExplorerCollection))
        {
            SelectedExplorerCollection = "All Folders";
        }

        // 2. Select folders based on active collection pill
        IEnumerable<FolderCategory> items;
        if (SelectedExplorerCollection == "All Folders")
        {
            items = Collections.SelectMany(c => c.Folders).DistinctBy(f => f.Name);
        }
        else
        {
            var matchedCol = Collections.FirstOrDefault(c => string.Equals(c.Name, SelectedExplorerCollection, StringComparison.OrdinalIgnoreCase));
            items = matchedCol != null ? matchedCol.Folders : Enumerable.Empty<FolderCategory>();
        }

        // 3. Search query filter
        if (!string.IsNullOrWhiteSpace(ExplorerSearchText))
        {
            string q = ExplorerSearchText.Trim().ToLowerInvariant();
            items = items.Where(f => (!string.IsNullOrEmpty(f.Name) && f.Name.ToLowerInvariant().Contains(q)) ||
                                     (!string.IsNullOrEmpty(f.CollectionName) && f.CollectionName.ToLowerInvariant().Contains(q)) ||
                                     (!string.IsNullOrEmpty(f.Description) && f.Description.ToLowerInvariant().Contains(q)));
        }

        // 4. Apply sorting
        items = ExplorerSortMode switch
        {
            "Count" => items.OrderByDescending(f => f.Count).ThenBy(f => f.Name),
            "Recent" => items.OrderByDescending(f => f.LastUpdatedText).ThenBy(f => f.Name),
            _ => items.OrderBy(f => f.Name)
        };

        ExplorerDisplayFolders = new ObservableCollection<FolderCategory>(items);
    }

    [RelayCommand]
    public void SelectExplorerCollection(object? parameter)
    {
        string? colName = parameter switch
        {
            ExplorerPillItem pill => pill.Name,
            string s => s,
            _ => null
        };
        SelectedExplorerCollection = string.IsNullOrWhiteSpace(colName) ? "All Folders" : colName;
        UpdateExplorerDisplayFolders();
    }

    [RelayCommand]
    public void SetExplorerViewMode(string mode)
    {
        IsExplorerViewDetails = string.Equals(mode, "Details", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public void SetExplorerSort(string sortMode)
    {
        ExplorerSortMode = sortMode;
        UpdateExplorerDisplayFolders();
    }

    [RelayCommand]
    public void ClearExplorerSearch()
    {
        ExplorerSearchText = string.Empty;
        UpdateExplorerDisplayFolders();
    }

    [RelayCommand]
    public void SelectExplorerFolder(FolderCategory? folder)
    {
        SelectedExplorerFolder = folder;
        foreach (var f in ExplorerDisplayFolders)
        {
            f.IsSelected = (folder != null && string.Equals(f.Name, folder.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    [RelayCommand]
    public void NavigateExplorerUp()
    {
        if (SelectedExplorerCollection != "All Folders")
        {
            SelectedExplorerCollection = "All Folders";
            UpdateExplorerDisplayFolders();
        }
        else
        {
            NavigateHome();
        }
    }

    [RelayCommand]
    public void NavigateExplorerBack()
    {
        if (SelectedExplorerCollection != "All Folders")
        {
            SelectedExplorerCollection = "All Folders";
            UpdateExplorerDisplayFolders();
        }
        else
        {
            NavigateFeed();
        }
    }

    [RelayCommand]
    public void ShowFoldersHub()
    {
        ActiveRailView = "Folders";
        IsHomeDashboardActive = false;
        IsFoldersViewActive = true;

        if (IsTheaterModeOpen)
        {
            CloseTheaterMode();
        }
        UpdateExplorerDisplayFolders();
        StatusMessage = "Collections & Folders Explorer.";
    }

    [RelayCommand]
    public void OpenFolderFromHub(FolderCategory? folder)
    {
        if (folder == null) return;
        IsHomeDashboardActive = false;
        IsFoldersViewActive = false;
        ActiveRailView = "Feed";
        SelectFolder(folder);
    }

    [RelayCommand]
    public void SetTheme(string themeName)
    {
        SelectedThemeName = themeName;
        StatusMessage = $"Theme set to {themeName}";
    }

    [RelayCommand]
    public void NavigateMediaGallery()
    {
        ActiveRailView = "Media";
        IsHomeDashboardActive = false;
        IsFoldersViewActive = false;
        IsGridView = true;
        SearchQuery = string.Empty;
        SelectedTag = null;
        SelectedFolder = null;
        FilteredPosts = new ObservableCollection<Post>(Posts.Where(p => p.HasMedia));
        SelectedPost = FilteredPosts.FirstOrDefault();
        if (IsTheaterModeOpen) CloseTheaterMode();
        StatusMessage = $"Media Gallery — Showing {FilteredPosts.Count} posts with cached media assets.";
    }

    [RelayCommand]
    public void NavigateTags()
    {
        ActiveRailView = "Tags";
        IsHomeDashboardActive = false;
        IsFoldersViewActive = false;
        if (Tags.Count > 0)
        {
            SelectTag(Tags[0]);
        }
        else
        {
            NavigateFeed();
        }
    }

    [RelayCommand]
    public void OpenImportArchive()
    {
        var vm = new ImportArchiveViewModel(_tweetImporterService, this);
        var win = new Views.ImportArchiveWindow(vm)
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    [RelayCommand]
    public async Task OpenThreadImporterAsync()
    {
        var existingFolderNames = Folders.Select(f => f.Name).ToList();
        var vm = new ImportThreadViewModel(_dbService, existingFolderNames, DefaultFolder);
        var win = new Views.ImportThreadWindow(vm)
        {
            Owner = Application.Current.MainWindow
        };

        bool? result = win.ShowDialog();
        if (result == true && vm.ImportedPosts.Count > 0)
        {
            // ⚡ Highly optimized in-memory update (Zero disk re-querying!)
            foreach (var p in vm.ImportedPosts.AsEnumerable().Reverse())
            {
                Posts.Insert(0, p);
            }

            EnsureFolderExistsInHierarchy(vm.ImportedPosts[0].FolderName);
            await UpdateCountsAndSizeAsync();

            string threadId = vm.ImportedPosts[0].ThreadId!;
            SearchQuery = $"thread:{threadId}";
            SelectedPost = vm.ImportedPosts[0];
            StatusMessage = $"Imported thread with {vm.ImportedPosts.Count} tweets by {vm.AuthorHandle}!";
        }
    }

    public void EnsureFolderExistsInHierarchy(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || string.Equals(folderName, "All Posts", StringComparison.OrdinalIgnoreCase))
            return;

        bool exists = Collections.Any(c => c.Folders.Any(f => string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase)));
        if (!exists)
        {
            var targetCol = Collections.FirstOrDefault(c => c.Name == "Engineering & Tech") ?? Collections.FirstOrDefault();
            if (targetCol == null)
            {
                targetCol = new CollectionGroup { Name = "General Archive", IconGlyph = "🗂️", AccentColor = "#1D9BF0" };
                Collections.Add(targetCol);
            }

            targetCol.Folders.Add(new FolderCategory
            {
                Name = folderName,
                CollectionName = targetCol.Name,
                IconGlyph = "📁",
                AccentColor = targetCol.AccentColor
            });

            RebuildFlatFolders();
            SaveHierarchyConfig();
        }
    }

    [RelayCommand]
    public void ZoomInGrid()
    {
        GridCardWidth = Math.Min(520, GridCardWidth + 35);
    }

    [RelayCommand]
    public void ZoomOutGrid()
    {
        GridCardWidth = Math.Max(180, GridCardWidth - 35);
    }

    [RelayCommand]
    public void SetAppScale(string scaleStr)
    {
        if (double.TryParse(scaleStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s))
        {
            AppScale = Math.Clamp(s, 0.75, 2.0);
        }
    }

    [RelayCommand]
    public void ZoomInApp()
    {
        AppScale = Math.Min(1.75, Math.Round(AppScale + 0.10, 2));
    }

    [RelayCommand]
    public void ZoomOutApp()
    {
        AppScale = Math.Max(0.85, Math.Round(AppScale - 0.10, 2));
    }

    [RelayCommand]
    public void ResetAppScale()
    {
        AppScale = 1.25;
    }

    // ========== PANEL TOGGLES ==========
    [RelayCommand]
    public void ToggleInspector()
    {
        IsInspectorVisible = !IsInspectorVisible;
    }

    [RelayCommand]
    public void CloseInspector()
    {
        IsInspectorVisible = false;
    }

    [RelayCommand]
    public void OpenInspector()
    {
        IsInspectorVisible = true;
    }

    [RelayCommand]
    public void ToggleTags()
    {
        IsTagsVisible = !IsTagsVisible;
    }

    // ========== SETTINGS ==========
    [RelayCommand]
    public void OpenSettings()
    {
        var settingsVm = new SettingsViewModel(_dbService, _securityService, _settingsService, Folders);
        var settingsWin = new Views.SettingsWindow(settingsVm)
        {
            Owner = Application.Current.MainWindow
        };

        if (settingsWin.ShowDialog() == true)
        {
            IsGridView = settingsVm.PreferGridView;
            IsInspectorVisible = settingsVm.ShowInspectorByDefault;
            IsTagsVisible = settingsVm.ShowTagsByDefault;
            DefaultFolder = settingsVm.SelectedDefaultFolder;
            AutoExtractHashtags = settingsVm.AutoExtractHashtags;
            StatusMessage = "Settings updated.";
        }
    }

    // ========== VAULT LOCK ==========
    [RelayCommand]
    public void LockVault()
    {
        if (!_securityService.HasPasswordSet)
        {
            var setPwdWin = new Views.SetPasswordWindow(_securityService)
            {
                Owner = Application.Current.MainWindow
            };
            if (setPwdWin.ShowDialog() != true) return;
        }

        var mainWindow = Application.Current.MainWindow;
        mainWindow?.Hide();

        var unlockWin = new Views.UnlockWindow(_securityService);
        bool? result = unlockWin.ShowDialog();

        if (result == true)
        {
            mainWindow?.Show();
            mainWindow?.Activate();
            StatusMessage = "Vault unlocked.";
        }
        else
        {
            Application.Current.Shutdown();
        }
    }

    // ========== VIEW MODE (List / Grid) ==========
    [RelayCommand]
    public void SetListView()
    {
        IsGridView = false;
    }

    [RelayCommand]
    public void SetGridView()
    {
        IsGridView = true;
    }

    // ========== THEATER MODE ACTIONS ==========
    [RelayCommand]
    public void OpenTheaterMode(Post? post)
    {
        var target = post ?? SelectedPost;
        if (target == null) return;

        TheaterPost = target;
        SelectedPost = target;
        CurrentTheaterMediaIndex = 0;
        UpdateTheaterMediaState();
        IsTheaterModeOpen = true;
    }

    [RelayCommand]
    public void CloseTheaterMode()
    {
        IsTheaterModeOpen = false;
        TheaterPost = null;
        CurrentTheaterMedia = null;
    }

    [RelayCommand]
    public void NextTheaterMedia()
    {
        if (TheaterPost == null || TheaterPost.MediaItems.Count <= 1) return;
        CurrentTheaterMediaIndex = (CurrentTheaterMediaIndex + 1) % TheaterPost.MediaItems.Count;
        UpdateTheaterMediaState();
    }

    [RelayCommand]
    public void PreviousTheaterMedia()
    {
        if (TheaterPost == null || TheaterPost.MediaItems.Count <= 1) return;
        CurrentTheaterMediaIndex = (CurrentTheaterMediaIndex - 1 + TheaterPost.MediaItems.Count) % TheaterPost.MediaItems.Count;
        UpdateTheaterMediaState();
    }

    private void UpdateTheaterMediaState()
    {
        if (TheaterPost != null && TheaterPost.MediaItems.Count > 0)
        {
            if (CurrentTheaterMediaIndex < 0 || CurrentTheaterMediaIndex >= TheaterPost.MediaItems.Count)
            {
                CurrentTheaterMediaIndex = 0;
            }

            CurrentTheaterMedia = TheaterPost.MediaItems[CurrentTheaterMediaIndex];
            HasMultipleTheaterMedia = TheaterPost.MediaItems.Count > 1;
            TheaterMediaCounter = $"{CurrentTheaterMediaIndex + 1} / {TheaterPost.MediaItems.Count}";
            IsTheaterMediaVideo = CurrentTheaterMedia.IsVideo;
        }
        else
        {
            CurrentTheaterMedia = null;
            HasMultipleTheaterMedia = false;
            TheaterMediaCounter = string.Empty;
            IsTheaterMediaVideo = false;
        }
    }

    [RelayCommand]
    public void CopyPostText(Post? post)
    {
        var target = post ?? TheaterPost ?? SelectedPost;
        if (target == null || string.IsNullOrWhiteSpace(target.Content)) return;

        try
        {
            Clipboard.SetText(target.Content);
            StatusMessage = "Copied tweet content to clipboard.";
        }
        catch { }
    }

    [RelayCommand]
    public void OpenLocalFolder(Post? post)
    {
        var target = post ?? TheaterPost ?? SelectedPost;
        if (target == null || string.IsNullOrWhiteSpace(target.FolderPath))
        {
            MessageBox.Show("No media folder path exists for this entry.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (!Directory.Exists(target.FolderPath))
            {
                Directory.CreateDirectory(target.FolderPath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{target.FolderPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedPostsAsync()
    {
        var toDelete = SelectedPosts.ToList();
        if (toDelete.Count == 0 && SelectedPost != null)
        {
            toDelete.Add(SelectedPost);
        }

        if (toDelete.Count == 0) return;

        if (toDelete.Count == 1)
        {
            await DeletePostAsync(toDelete[0]);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to permanently delete {toDelete.Count} selected posts?\n\nThis will remove them from the database and free up storage.",
            $"Confirm Delete ({toDelete.Count} Posts)",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var ids = toDelete.Select(p => p.Id).ToList();
            await _dbService.DeletePostsAsync(ids);

            foreach (var p in toDelete)
            {
                Posts.Remove(p);
                FilteredPosts.Remove(p);
                if (TheaterPost == p)
                {
                    CloseTheaterMode();
                }
            }

            SelectedPosts.Clear();
            RequestClearSelection?.Invoke();
            OnPropertyChanged(nameof(SelectedPostsCount));
            OnPropertyChanged(nameof(HasMultipleSelection));
            OnPropertyChanged(nameof(SelectionStatusText));
            OnPropertyChanged(nameof(DeleteSingleMenuHeader));

            await UpdateCountsAndSizeAsync();

            SelectedPost = FilteredPosts.FirstOrDefault();
            StatusMessage = $"Deleted {toDelete.Count} posts successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete Error: {ex.Message}";
            MessageBox.Show($"Failed to delete selected posts:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task DeletePostAsync(Post? post)
    {
        if (post == null && HasMultipleSelection)
        {
            await DeleteSelectedPostsAsync();
            return;
        }

        var target = post ?? TheaterPost ?? SelectedPost;
        if (target == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete this archived post by {target.AuthorName}?\n\nThis will remove the entry from the database.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _dbService.DeletePostAsync(target.Id);
            Posts.Remove(target);
            FilteredPosts.Remove(target);

            if (SelectedPosts.Contains(target))
            {
                SelectedPosts.Remove(target);
                OnPropertyChanged(nameof(SelectedPostsCount));
                OnPropertyChanged(nameof(HasMultipleSelection));
                OnPropertyChanged(nameof(SelectionStatusText));
                OnPropertyChanged(nameof(DeleteSingleMenuHeader));
            }

            await UpdateCountsAndSizeAsync();

            if (TheaterPost == target)
            {
                CloseTheaterMode();
            }

            SelectedPost = FilteredPosts.FirstOrDefault();
            StatusMessage = "Post deleted successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete Error: {ex.Message}";
            MessageBox.Show($"Failed to delete post:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task EditPostAsync(Post? post)
    {
        var target = post ?? TheaterPost ?? SelectedPost;
        if (target == null) return;

        var availableNames = Folders.Where(f => f.Name != "All Posts").Select(f => f.Name).ToList();
        var editVm = new EditPostViewModel(target, availableNames);
        var editWindow = new Views.EditPostWindow(editVm)
        {
            Owner = Application.Current.MainWindow
        };

        if (editWindow.ShowDialog() == true)
        {
            await _dbService.UpdatePostAsync(target);
            await UpdateCountsAndSizeAsync();
            ApplyFilter();
            StatusMessage = "Post updated successfully.";
        }
    }

    [RelayCommand]
    public void TogglePostComments(Post? post)
    {
        var target = post ?? TheaterPost ?? SelectedPost;
        if (target == null) return;
        target.IsCommentsExpanded = !target.IsCommentsExpanded;
    }

    [RelayCommand]
    public async Task OpenPasteCommentsWindowAsync(Post? post)
    {
        var target = post ?? TheaterPost ?? SelectedPost;
        if (target == null) return;

        var vm = new PasteCommentsViewModel(target, _dbService);
        var win = new Views.PasteCommentsWindow(vm)
        {
            Owner = Application.Current.MainWindow
        };

        if (win.ShowDialog() == true)
        {
            target.RefreshCommentCounts();
            target.IsCommentsExpanded = true;
            await UpdateCountsAndSizeAsync();
            ApplyFilter();
            StatusMessage = $"Saved {target.CommentCount} comments for post by {target.AuthorName}.";
        }
    }

    [RelayCommand]
    public async Task QuickPasteClipboardCommentsAsync(Post? post)
    {
        var target = post ?? TheaterPost ?? SelectedPost;
        if (target == null) return;

        try
        {
            if (!Clipboard.ContainsText())
            {
                StatusMessage = "No text found in clipboard to paste.";
                return;
            }

            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                StatusMessage = "Clipboard text is empty.";
                return;
            }

            var parsed = CommentParserHelper.Parse(text, target.Id);
            if (parsed.Count == 0)
            {
                parsed.Add(new PostComment
                {
                    PostId = target.Id,
                    AuthorHandle = "@commenter",
                    AuthorName = "Commenter",
                    Content = text.Trim(),
                    TimeAgo = "just now"
                });
            }

            foreach (var c in parsed)
            {
                c.PostId = target.Id;
                target.Comments.Add(c);
            }

            target.RefreshCommentCounts();
            target.IsCommentsExpanded = true;
            await _dbService.SaveCommentsForPostAsync(target.Id, target.Comments);
            await UpdateCountsAndSizeAsync();
            ApplyFilter();
            StatusMessage = $"Successfully imported {parsed.Count} comments from clipboard!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Paste Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task AddInlineCommentAsync(object? parameter)
    {
        Post? targetPost = null;
        string? commentText = null;

        if (parameter is object[] arr && arr.Length >= 2)
        {
            targetPost = arr[0] as Post;
            commentText = arr[1]?.ToString();
        }
        else if (parameter is Post p)
        {
            targetPost = p;
        }

        targetPost ??= TheaterPost ?? SelectedPost;
        if (targetPost == null || string.IsNullOrWhiteSpace(commentText)) return;

        var comment = new PostComment
        {
            PostId = targetPost.Id,
            AuthorHandle = "@offline",
            AuthorName = "Offline User",
            Content = commentText.Trim(),
            TimeAgo = "just now"
        };

        targetPost.Comments.Add(comment);
        targetPost.RefreshCommentCounts();
        targetPost.IsCommentsExpanded = true;
        await _dbService.AddCommentToPostAsync(targetPost.Id, comment);
        StatusMessage = "Comment added.";
    }

    [RelayCommand]
    public async Task DeleteCommentAsync(object? parameter)
    {
        if (parameter is PostComment comment)
        {
            var parentPost = Posts.FirstOrDefault(p => p.Id == comment.PostId) 
                             ?? (SelectedPost?.Id == comment.PostId ? SelectedPost : null)
                             ?? (TheaterPost?.Id == comment.PostId ? TheaterPost : null);

            if (parentPost != null)
            {
                parentPost.Comments.Remove(comment);
                parentPost.RefreshCommentCounts();
            }

            await _dbService.DeleteCommentAsync(comment.Id);
            StatusMessage = "Comment deleted.";
        }
    }

    [RelayCommand]
    public async Task CreateNewPostAsync()
    {
        string defaultFolder = (SelectedFolder != null && SelectedFolder.Name != "All Posts") ? SelectedFolder.Name : "Coding";
        var availableNames = Folders.Where(f => f.Name != "All Posts").Select(f => f.Name).ToList();
        var newVm = new NewTweetViewModel(defaultFolder, availableNames);
        var newWindow = new Views.NewTweetWindow(newVm)
        {
            Owner = Application.Current.MainWindow
        };

        if (newWindow.ShowDialog() == true)
        {
            StatusMessage = "Archiving new tweet...";
            var post = await _clipboardService.CreateManualPostAsync(
                newVm.AuthorName,
                newVm.AuthorHandle,
                newVm.Content,
                newVm.FolderName,
                newVm.Tags,
                newVm.AttachedImagePaths,
                string.IsNullOrWhiteSpace(newVm.Comment) ? null : newVm.Comment.Trim()
            );

            if (post != null)
            {
                Posts.Insert(0, post);
                await UpdateCountsAndSizeAsync();
                ApplyFilter();
                SelectedPost = post;
                StatusMessage = $"Archived tweet by {post.AuthorName} successfully!";
            }
        }
    }

    [RelayCommand]
    public async Task MoveSelectedPostsToFolderAsync(object? parameter)
    {
        string? targetFolder = parameter as string;
        if (string.IsNullOrWhiteSpace(targetFolder) || targetFolder == "All Posts") return;

        var postsToMove = SelectedPosts.Count > 0 
            ? SelectedPosts.ToList() 
            : (SelectedPost != null ? new List<Post> { SelectedPost } : new List<Post>());

        if (postsToMove.Count == 0) return;

        int count = 0;
        foreach (var p in postsToMove)
        {
            if (!string.Equals(p.FolderName, targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                p.FolderName = targetFolder;
                await _dbService.UpdatePostAsync(p);
                count++;
            }
        }

        await UpdateCountsAndSizeAsync();
        ApplyFilter();
        OnPropertyChanged(nameof(SelectedPostStoryParts));
        StatusMessage = $"Moved {count} post(s) to folder '{targetFolder}'.";
    }

    [RelayCommand]
    public async Task MoveThreadToFolderAsync(object? parameter)
    {
        Post? targetPost = null;
        string? targetFolder = null;

        if (parameter is object[] arr && arr.Length >= 2)
        {
            targetPost = arr[0] as Post;
            targetFolder = arr[1]?.ToString();
        }
        else if (parameter is string folder)
        {
            targetFolder = folder;
        }

        if (targetPost == null) targetPost = SelectedPost ?? TheaterPost;
        if (targetPost == null || string.IsNullOrWhiteSpace(targetFolder) || targetFolder == "All Posts") return;

        if (!targetPost.IsPartOfThread || string.IsNullOrEmpty(targetPost.ThreadId))
        {
            await MovePostToFolderAsync(new object[] { targetPost, targetFolder });
            return;
        }

        string threadId = targetPost.ThreadId;
        var threadPosts = Posts.Where(p => p.ThreadId == threadId).ToList();
        if (threadPosts.Count == 0) threadPosts.Add(targetPost);

        int movedCount = 0;
        foreach (var p in threadPosts)
        {
            if (!string.Equals(p.FolderName, targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                p.FolderName = targetFolder;
                await _dbService.UpdatePostAsync(p);
                movedCount++;
            }
        }

        await UpdateCountsAndSizeAsync();
        ApplyFilter();
        OnPropertyChanged(nameof(SelectedPostStoryParts));
        StatusMessage = $"Moved entire thread ({threadPosts.Count} parts) by {targetPost.AuthorName} to folder '{targetFolder}'.";
    }

    [RelayCommand]
    public async Task MovePostToFolderAsync(object? parameter)
    {
        Post? targetPost = null;
        string? targetFolder = null;

        if (parameter is object[] arr && arr.Length >= 2)
        {
            targetPost = arr[0] as Post;
            targetFolder = arr[1]?.ToString();
        }
        else if (parameter is Post p)
        {
            targetPost = p;
        }
        else if (parameter is string folder)
        {
            targetFolder = folder;
        }

        // If multiple posts are selected and a destination folder is provided, move all selected posts!
        if (SelectedPosts.Count > 1 && !string.IsNullOrWhiteSpace(targetFolder))
        {
            await MoveSelectedPostsToFolderAsync(targetFolder);
            return;
        }

        if (targetPost == null) targetPost = SelectedPost ?? TheaterPost;
        if (targetPost == null) return;

        if (string.IsNullOrWhiteSpace(targetFolder) || targetFolder == "All Posts")
        {
            var available = DestinationFolders.Where(f => f.Name != targetPost.FolderName).Select(f => f.Name).ToList();
            if (available.Count == 0) return;
            targetFolder = available[0];
        }

        targetPost.FolderName = targetFolder;
        await _dbService.UpdatePostAsync(targetPost);
        await UpdateCountsAndSizeAsync();
        ApplyFilter();
        OnPropertyChanged(nameof(SelectedPostStoryParts));
        StatusMessage = $"Moved post by {targetPost.AuthorName} to folder '{targetFolder}'.";
    }

    // ========== STORY LINKING COMMANDS & STATE ==========
    [RelayCommand]
    public async Task LinkSelectedPostsAsStoryAsync()
    {
        if (SelectedPosts.Count < 2)
        {
            MessageBox.Show("Please select 2 or more posts (using Ctrl+Click or Shift+Click) to link them together as a multi-part story.", "Link Story", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ordered = SelectedPosts.OrderBy(p => p.PostDate).ToList();
        string newThreadId = Guid.NewGuid().ToString("N");

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].ThreadId = newThreadId;
            ordered[i].ThreadIndex = i + 1;
            await _dbService.UpdatePostAsync(ordered[i]);
        }

        ApplyFilter();
        OnPropertyChanged(nameof(SelectedPostStoryParts));
        StatusMessage = $"Successfully linked {ordered.Count} posts as a multi-part story (Parts 1 to {ordered.Count}).";
        MessageBox.Show($"Linked {ordered.Count} posts into a story sequence!\n\nYou can now browse between parts seamlessly across the cards, detail pane, and theater mode.", "Story Linked", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    public async Task UnlinkFromStoryAsync(object? parameter)
    {
        var targetPosts = new List<Post>();
        if (parameter is Post p)
        {
            targetPosts.Add(p);
        }
        else if (SelectedPosts.Count > 0)
        {
            targetPosts.AddRange(SelectedPosts);
        }
        else if (SelectedPost != null)
        {
            targetPosts.Add(SelectedPost);
        }

        if (targetPosts.Count == 0) return;

        int unlinkedCount = 0;
        foreach (var post in targetPosts)
        {
            if (post.IsPartOfThread)
            {
                post.ThreadId = null;
                post.ThreadIndex = 0;
                await _dbService.UpdatePostAsync(post);
                unlinkedCount++;
            }
        }

        ApplyFilter();
        OnPropertyChanged(nameof(SelectedPostStoryParts));
        StatusMessage = $"Unlinked {unlinkedCount} post(s) from story.";
    }

    [RelayCommand]
    public void ViewFullStory(object? parameter)
    {
        Post? post = parameter as Post ?? SelectedPost;
        if (post == null || !post.IsPartOfThread) return;

        SearchQuery = $"thread:{post.ThreadId}";
        StatusMessage = $"Displaying all parts of story ({FilteredPosts.Count} parts).";
    }

    [RelayCommand]
    public void OpenThreadReader(object? parameter)
    {
        Post? target = parameter as Post ?? SelectedPost ?? TheaterPost;
        if (target == null) return;

        List<Post> threadPosts;
        if (target.IsPartOfThread && !string.IsNullOrEmpty(target.ThreadId))
        {
            threadPosts = Posts.Where(p => p.ThreadId == target.ThreadId)
                               .OrderBy(p => p.ThreadIndex)
                               .ToList();
        }
        else
        {
            threadPosts = new List<Post> { target };
        }

        if (threadPosts.Count == 0) return;

        var availableFolderNames = DestinationFolders.Select(f => f.Name).ToList();
        var vm = new ThreadReaderViewModel(threadPosts, availableFolderNames, async (newFolder) =>
        {
            await MoveThreadToFolderAsync(new object[] { threadPosts.First(), newFolder });
        });
        var win = new Views.ThreadReaderWindow(vm);
        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            win.Owner = Application.Current.MainWindow;
        }
        win.Show();
        StatusMessage = $"Opened Thread Reader for {threadPosts.Count} tweets by {vm.AuthorHandle}.";
    }

    [RelayCommand]
    public void OpenStoryExport(object? parameter)
    {
        Post? target = parameter as Post ?? SelectedPost ?? TheaterPost;
        List<Post>? threadPosts = null;

        if (target != null)
        {
            if (target.IsPartOfThread && !string.IsNullOrEmpty(target.ThreadId))
            {
                threadPosts = Posts.Where(p => p.ThreadId == target.ThreadId)
                                   .OrderBy(p => p.ThreadIndex)
                                   .ToList();
            }
            else
            {
                threadPosts = new List<Post> { target };
            }
        }

        var vm = new StoryExportViewModel(initialPosts: threadPosts);
        var win = new Views.StoryExportWindow(vm);
        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            win.Owner = Application.Current.MainWindow;
        }
        win.ShowDialog();
    }

    [RelayCommand]
    public void OpenStoryCanvas(object? parameter)
    {
        // When clicked from toolbar or shortcut with no parameter, open a fresh BLANK canvas
        Post? target = parameter as Post;
        List<Post>? threadPosts = null;

        if (target != null)
        {
            if (target.IsPartOfThread && !string.IsNullOrEmpty(target.ThreadId))
            {
                threadPosts = Posts.Where(p => p.ThreadId == target.ThreadId)
                                   .OrderBy(p => p.ThreadIndex)
                                   .ToList();
            }
            else
            {
                threadPosts = new List<Post> { target };
            }
        }

        var availableFolderNames = DestinationFolders.Select(f => f.Name).ToList();
        var vm = new StoryCanvasViewModel(_dbService, availableFolderNames, threadPosts);
        var win = new Views.StoryCanvasWindow(vm);
        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            win.Owner = Application.Current.MainWindow;
        }
        win.Show();
    }

    [RelayCommand]
    public void OpenDocumentReader(object? parameter)
    {
        Post? target = parameter as Post ?? SelectedPost ?? TheaterPost;
        if (target == null) return;

        List<Post> threadPosts;
        if (target.IsPartOfThread && !string.IsNullOrEmpty(target.ThreadId))
        {
            threadPosts = Posts.Where(p => p.ThreadId == target.ThreadId)
                               .OrderBy(p => p.ThreadIndex)
                               .ToList();
        }
        else
        {
            threadPosts = new List<Post> { target };
        }

        if (threadPosts.Count == 0) return;

        var vm = new StoryDocumentReaderViewModel(threadPosts);
        var win = new Views.StoryDocumentReaderWindow(vm);
        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            win.Owner = Application.Current.MainWindow;
        }
        win.Show();
    }

    [RelayCommand]
    public void JumpToStoryPart(object? parameter)
    {
        if (parameter is Post targetPost)
        {
            SelectedPost = targetPost;
            StatusMessage = $"Viewing Part {targetPost.ThreadIndex}.";
        }
        else if (parameter is int targetIndex && SelectedPost != null && SelectedPost.IsPartOfThread)
        {
            var match = Posts.FirstOrDefault(p => p.ThreadId == SelectedPost.ThreadId && p.ThreadIndex == targetIndex);
            if (match != null)
            {
                SelectedPost = match;
                StatusMessage = $"Viewing Part {match.ThreadIndex}.";
            }
        }
    }

    private ObservableCollection<Post>? _cachedStoryParts;
    private string? _cachedStoryThreadId;

    public ObservableCollection<Post> SelectedPostStoryParts
    {
        get
        {
            if (SelectedPost == null || !SelectedPost.IsPartOfThread || string.IsNullOrEmpty(SelectedPost.ThreadId))
            {
                _cachedStoryParts = null;
                _cachedStoryThreadId = null;
                return new ObservableCollection<Post>();
            }

            if (_cachedStoryParts != null && _cachedStoryThreadId == SelectedPost.ThreadId)
            {
                return _cachedStoryParts;
            }

            var parts = Posts
                .Where(p => p.ThreadId == SelectedPost.ThreadId)
                .OrderBy(p => p.ThreadIndex)
                .ToList();

            _cachedStoryParts = new ObservableCollection<Post>(parts);
            _cachedStoryThreadId = SelectedPost.ThreadId;
            return _cachedStoryParts;
        }
    }


    [RelayCommand]
    public void SelectNextLinkedPart()
    {
        if (SelectedPost == null || !SelectedPost.IsPartOfThread) return;
        var next = FilteredPosts.FirstOrDefault(p => p.ThreadId == SelectedPost.ThreadId && p.ThreadIndex == SelectedPost.ThreadIndex + 1)
                   ?? Posts.FirstOrDefault(p => p.ThreadId == SelectedPost.ThreadId && p.ThreadIndex == SelectedPost.ThreadIndex + 1);
        if (next != null)
        {
            SelectedPost = next;
            StatusMessage = $"Navigated to Part {next.ThreadIndex}.";
        }
    }

    [RelayCommand]
    public void SelectPreviousLinkedPart()
    {
        if (SelectedPost == null || !SelectedPost.IsPartOfThread) return;
        var prev = FilteredPosts.FirstOrDefault(p => p.ThreadId == SelectedPost.ThreadId && p.ThreadIndex == SelectedPost.ThreadIndex - 1)
                   ?? Posts.FirstOrDefault(p => p.ThreadId == SelectedPost.ThreadId && p.ThreadIndex == SelectedPost.ThreadIndex - 1);
        if (prev != null)
        {
            SelectedPost = prev;
            StatusMessage = $"Navigated to Part {prev.ThreadIndex}.";
        }
    }



}

// ========== CONFIG DTOs ==========
public class CollectionDto
{
    public string Name { get; set; } = string.Empty;
    public string? IconGlyph { get; set; }
    public string? Description { get; set; }
    public string? AccentColor { get; set; }
    public bool IsDeletable { get; set; } = true;
    public List<FolderDto> Folders { get; set; } = new();
}

public class FolderDto
{
    public string Name { get; set; } = string.Empty;
    public string? IconGlyph { get; set; }
    public string? Description { get; set; }
    public string? AccentColor { get; set; }
}

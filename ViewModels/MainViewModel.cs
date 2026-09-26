using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSocialArchive.Models;
using LocalSocialArchive.Services;

namespace LocalSocialArchive.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IEseDatabaseService _dbService;
    private readonly IClipboardService _clipboardService;
    private readonly string _configFilePath;

    [ObservableProperty]
    private ObservableCollection<Post> _posts = new();

    [ObservableProperty]
    private ObservableCollection<Post> _filteredPosts = new();

    [ObservableProperty]
    private Post? _selectedPost;

    // ========== TOP-LEVEL COLLECTIONS (Each contains a group of Folders) ==========
    [ObservableProperty]
    private ObservableCollection<CollectionGroup> _collections = new();

    [ObservableProperty]
    private CollectionGroup? _selectedCollection;

    // ========== FLAT FOLDERS (For quick lookup, filter dropdowns, and "All Posts") ==========
    [ObservableProperty]
    private ObservableCollection<FolderCategory> _folders = new();

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

    // ========== PANEL VISIBILITY (Inspector, Tags & Collections Hub) ==========
    [ObservableProperty]
    private bool _isInspectorVisible = true;

    [ObservableProperty]
    private bool _isTagsVisible = true;

    [ObservableProperty]
    private bool _isFoldersViewActive;

    // ========== THREAD CANVAS STUDIO (Visual Sequential Tweet Scratchpad) ==========
    [ObservableProperty]
    private bool _isCanvasViewActive;

    [ObservableProperty]
    private ObservableCollection<ThreadNode> _canvasThreadNodes = new();

    [ObservableProperty]
    private string _canvasThreadTitle = "New Twitter / X Thread";

    [ObservableProperty]
    private string _canvasDefaultAuthor = "Offline User";

    [ObservableProperty]
    private string _canvasDefaultHandle = "@offline";

    [ObservableProperty]
    private string _canvasSelectedFolder = "Coding";

    [ObservableProperty]
    private string _canvasDefaultTags = "#thread #offline";

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

    public MainViewModel(IEseDatabaseService dbService, IClipboardService clipboardService)
    {
        _dbService = dbService;
        _clipboardService = clipboardService;

        string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string baseDir = Path.Combine(userDocs, "LocalSocialArchive");
        _configFilePath = Path.Combine(baseDir, "collections_config.json");

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

        if (SelectedFolder == null || !Folders.Any(f => f.Name == SelectedFolder.Name))
        {
            SelectedFolder = Folders[0];
            SelectedFolder.IsSelected = true;
        }

        UpdateFilterDropdownOptions();
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

        var folderCounts = await _dbService.GetFolderCountsAsync();

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

                // Extract up to 3 media preview paths for this folder
                var folderPosts = Posts.Where(p => string.Equals(p.FolderName, folder.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                var thumbs = new ObservableCollection<string>();
                foreach (var p in folderPosts)
                {
                    foreach (var media in p.MediaItems)
                    {
                        if (!string.IsNullOrWhiteSpace(media.FilePath) && !thumbs.Contains(media.FilePath))
                        {
                            thumbs.Add(media.FilePath);
                            if (thumbs.Count >= 3) break;
                        }
                    }
                    if (thumbs.Count >= 3) break;
                }
                folder.RecentThumbnails = thumbs;

                if (folderPosts.Count > 0)
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

        var tagCounts = await _dbService.GetTagCountsAsync();
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
    }

    [RelayCommand]
    public async Task PasteFromClipboardAsync()
    {
        StatusMessage = "Processing clipboard contents...";
        try
        {
            string targetFolder = (SelectedFolder != null && SelectedFolder.Name != "All Posts") ? SelectedFolder.Name : "Coding";
            var post = await _clipboardService.ProcessClipboardAsync(targetFolder);

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

        IsFoldersViewActive = false;
        IsCanvasViewActive = false;
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

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<Post> query = Posts;

        if (SelectedFolder != null && SelectedFolder.Name != "All Posts")
        {
            query = query.Where(p => string.Equals(p.FolderName, SelectedFolder.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedTag != null)
        {
            string tagName = SelectedTag.Name.TrimStart('#').ToLowerInvariant();
            query = query.Where(p => p.TagList.Any(t => string.Equals(t, tagName, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Content.ToLowerInvariant().Contains(q) ||
                p.AuthorName.ToLowerInvariant().Contains(q) ||
                p.AuthorHandle.ToLowerInvariant().Contains(q) ||
                p.FolderName.ToLowerInvariant().Contains(q) ||
                p.TagList.Any(t => t.ToLowerInvariant().Contains(q))
            );
        }

        FilteredPosts = new ObservableCollection<Post>(query);

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
                return;
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
        }
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

    // ========== NAVIGATION & HOME ==========
    [RelayCommand]
    public void NavigateHome()
    {
        IsFoldersViewActive = false;
        IsCanvasViewActive = false;
        if (Folders.Count > 0)
        {
            SelectFolder(Folders[0]);
        }
        SearchQuery = string.Empty;
        SelectedTag = null;
        foreach (var t in Tags) t.IsSelected = false;
        ApplyFilter();

        if (IsTheaterModeOpen)
        {
            CloseTheaterMode();
        }

        SelectedPost = FilteredPosts.FirstOrDefault();
        StatusMessage = "Showing all collections, folders & archived posts.";
    }

    // ========== COLLECTIONS HUB SECTION (Collections containing Folders) ==========
    [RelayCommand]
    public void ShowFoldersHub()
    {
        IsFoldersViewActive = true;
        IsCanvasViewActive = false;
        if (IsTheaterModeOpen)
        {
            CloseTheaterMode();
        }
        StatusMessage = "Collections Hub — Overview of all top-level collections and their folders.";
    }

    [RelayCommand]
    public void OpenFolderFromHub(FolderCategory? folder)
    {
        if (folder == null) return;
        IsFoldersViewActive = false;
        IsCanvasViewActive = false;
        SelectFolder(folder);
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
        var settingsVm = new SettingsViewModel(_dbService, Folders, IsGridView, IsInspectorVisible, IsTagsVisible);
        var settingsWin = new Views.SettingsWindow(settingsVm)
        {
            Owner = Application.Current.MainWindow
        };

        if (settingsWin.ShowDialog() == true)
        {
            IsGridView = settingsVm.PreferGridView;
            IsInspectorVisible = settingsVm.ShowInspectorByDefault;
            IsTagsVisible = settingsVm.ShowTagsByDefault;
            StatusMessage = "Settings updated.";
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
    public async Task DeletePostAsync(Post? post)
    {
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
                newVm.AttachedImagePaths
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

    // ========== THREAD CANVAS STUDIO COMMANDS & LOGIC ==========
    [RelayCommand]
    public void ShowThreadCanvas()
    {
        IsCanvasViewActive = true;
        IsFoldersViewActive = false;
        if (IsTheaterModeOpen)
        {
            CloseTheaterMode();
        }

        if (SelectedFolder != null && SelectedFolder.Name != "All Posts")
        {
            CanvasSelectedFolder = SelectedFolder.Name;
        }

        StatusMessage = "Word Canvas Studio — Hold images, text, GIFs, and videos like Microsoft Word.";
    }

    public async Task<Post?> ArchiveWordDocumentAsync(string title, string folder, string authorName, string authorHandle, string tags, string content, List<string> mediaPaths)
    {
        IsLoading = true;
        StatusMessage = "Archiving document canvas to ESE database...";
        try
        {
            string targetFolder = string.IsNullOrWhiteSpace(folder) ? "Coding" : folder.Trim();
            string finalContent = content;
            if (!string.IsNullOrWhiteSpace(title) && title != "Untitled Document" && title != "New Twitter / X Thread")
            {
                finalContent = $"# {title}\n\n{content}".Trim();
            }

            if (string.IsNullOrWhiteSpace(finalContent) && (mediaPaths == null || mediaPaths.Count == 0))
            {
                MessageBox.Show("Cannot archive an empty document canvas. Add some text, images, or GIFs first!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            var post = await _clipboardService.CreateManualPostAsync(
                string.IsNullOrWhiteSpace(authorName) ? "Offline User" : authorName.Trim(),
                string.IsNullOrWhiteSpace(authorHandle) ? "@offline" : authorHandle.Trim(),
                string.IsNullOrWhiteSpace(finalContent) ? "Archived Canvas Note" : finalContent,
                targetFolder,
                tags ?? "#archive",
                mediaPaths
            );

            if (post != null)
            {
                Posts.Insert(0, post);
                await UpdateCountsAndSizeAsync();
                ApplyFilter();
                SelectedPost = post;
                StatusMessage = $"Document archived to '{targetFolder}' with {post.MediaItems.Count} media item(s)!";
                return post;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Archive Error: {ex.Message}";
            MessageBox.Show($"Failed to archive document:\n{ex.Message}", "Archive Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
        return null;
    }

    [RelayCommand]
    public void AddEmptyCanvasNode()
    {
        var node = new ThreadNode
        {
            StepNumber = CanvasThreadNodes.Count + 1,
            AuthorName = string.IsNullOrWhiteSpace(CanvasDefaultAuthor) ? "Offline User" : CanvasDefaultAuthor.Trim(),
            AuthorHandle = string.IsNullOrWhiteSpace(CanvasDefaultHandle) ? "@offline" : CanvasDefaultHandle.Trim(),
            Content = string.Empty,
            Tags = CanvasDefaultTags
        };
        CanvasThreadNodes.Add(node);
        StatusMessage = $"Added Step #{node.StepNumber} to Thread Canvas.";
    }

    [RelayCommand]
    public void RemoveCanvasNode(ThreadNode? node)
    {
        if (node == null) return;
        CanvasThreadNodes.Remove(node);
        RenumberCanvasNodes();
        StatusMessage = "Removed step from Thread Canvas.";
    }

    [RelayCommand]
    public void MoveNodeUp(ThreadNode? node)
    {
        if (node == null) return;
        int idx = CanvasThreadNodes.IndexOf(node);
        if (idx > 0)
        {
            CanvasThreadNodes.Move(idx, idx - 1);
            RenumberCanvasNodes();
            StatusMessage = $"Moved Step #{idx + 1} up to #{idx}.";
        }
    }

    [RelayCommand]
    public void MoveNodeDown(ThreadNode? node)
    {
        if (node == null) return;
        int idx = CanvasThreadNodes.IndexOf(node);
        if (idx >= 0 && idx < CanvasThreadNodes.Count - 1)
        {
            CanvasThreadNodes.Move(idx, idx + 1);
            RenumberCanvasNodes();
            StatusMessage = $"Moved Step #{idx + 1} down to #{idx + 2}.";
        }
    }

    [RelayCommand]
    public void AddMediaToNode(ThreadNode? node)
    {
        if (node == null) return;
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Attach Image or Video to Tweet Step",
                Filter = "All Supported Media (*.png;*.jpg;*.jpeg;*.webp;*.gif;*.mp4;*.webm)|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.mp4;*.webm|Images (*.png;*.jpg;*.jpeg;*.webp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.gif|Videos (*.mp4;*.webm)|*.mp4;*.webm|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
            {
                foreach (var file in dlg.FileNames)
                {
                    if (!node.AttachedImagePaths.Contains(file))
                    {
                        node.AttachedImagePaths.Add(file);
                    }
                }
                node.NotifyMediaChanged();
                StatusMessage = $"Attached {dlg.FileNames.Length} media item(s) to Step #{node.StepNumber}.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Media Attachment Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void RemoveMediaItem(object? parameter)
    {
        if (parameter is object[] arr && arr.Length == 2 && arr[0] is ThreadNode node && arr[1] is string filePath)
        {
            node.AttachedImagePaths.Remove(filePath);
            node.NotifyMediaChanged();
            StatusMessage = "Removed attached media from step.";
        }
        else if (parameter is string path)
        {
            foreach (var n in CanvasThreadNodes)
            {
                if (n.AttachedImagePaths.Contains(path))
                {
                    n.AttachedImagePaths.Remove(path);
                    n.NotifyMediaChanged();
                    break;
                }
            }
            StatusMessage = "Removed attached media.";
        }
    }

    [RelayCommand]
    public void ClearCanvas()
    {
        if (CanvasThreadNodes.Count == 0) return;
        var res = MessageBox.Show(
            "Are you sure you want to clear all steps from the Thread Canvas?",
            "Clear Canvas",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            CanvasThreadNodes.Clear();
            CanvasThreadTitle = "New Twitter / X Thread";
            StatusMessage = "Thread Canvas cleared.";
        }
    }

    [RelayCommand]
    public void PasteToCanvas()
    {
        try
        {
            string? text = null;
            System.Windows.Media.Imaging.BitmapSource? img = null;
            List<string>? files = null;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var drop = Clipboard.GetFileDropList();
                    files = new List<string>();
                    foreach (var f in drop)
                    {
                        if (!string.IsNullOrWhiteSpace(f)) files.Add(f);
                    }
                }
                if (Clipboard.ContainsImage())
                {
                    img = Clipboard.GetImage();
                }
                if (Clipboard.ContainsText())
                {
                    text = Clipboard.GetText();
                }
            });

            if (string.IsNullOrWhiteSpace(text) && img == null && (files == null || files.Count == 0))
            {
                StatusMessage = "Clipboard is empty or does not contain valid text, images, or files.";
                return;
            }

            var attachedMedia = new List<string>();
            string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string stagingDir = Path.Combine(userDocs, "LocalSocialArchive", "Media", "CanvasStaging");
            Directory.CreateDirectory(stagingDir);

            if (img != null)
            {
                string imgName = $"canvas_img_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}.png";
                string dest = Path.Combine(stagingDir, imgName);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                using (var fs = File.Create(dest))
                {
                    encoder.Save(fs);
                }
                attachedMedia.Add(dest);
            }

            if (files != null)
            {
                foreach (var f in files)
                {
                    if (File.Exists(f))
                    {
                        attachedMedia.Add(f);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                // Multi-tweet split check
                var splits = Regex.Split(text.Trim(), @"(?:\r?\n){2,}---\s*(?:\r?\n)+|(?:\r?\n){2,}(?=\d+/\d+|\(\d+/\d+\)|\d+\.)");
                if (splits.Length > 1)
                {
                    int addedCount = 0;
                    foreach (var part in splits)
                    {
                        if (string.IsNullOrWhiteSpace(part)) continue;
                        var (author, handle, content, tags) = ParseSocialMetadataHelper(part);
                        if (CanvasThreadNodes.Count == 0 && addedCount == 0)
                        {
                            if (!string.IsNullOrWhiteSpace(author) && author != "Offline User") CanvasDefaultAuthor = author;
                            if (!string.IsNullOrWhiteSpace(handle) && handle != "@offline") CanvasDefaultHandle = handle;
                            CanvasThreadTitle = SuggestTitle(content);
                        }

                        var node = new ThreadNode
                        {
                            StepNumber = CanvasThreadNodes.Count + 1,
                            AuthorName = string.IsNullOrWhiteSpace(author) || author == "Offline User" ? CanvasDefaultAuthor : author,
                            AuthorHandle = string.IsNullOrWhiteSpace(handle) || handle == "@offline" ? CanvasDefaultHandle : handle,
                            Content = content,
                            Tags = string.IsNullOrWhiteSpace(tags) ? CanvasDefaultTags : tags
                        };
                        CanvasThreadNodes.Add(node);
                        addedCount++;
                    }

                    if (attachedMedia.Count > 0 && CanvasThreadNodes.Count > 0)
                    {
                        foreach (var m in attachedMedia)
                        {
                            CanvasThreadNodes[CanvasThreadNodes.Count - 1].AttachedImagePaths.Add(m);
                        }
                        CanvasThreadNodes[CanvasThreadNodes.Count - 1].NotifyMediaChanged();
                    }

                    StatusMessage = $"Pasted multi-tweet thread ({addedCount} steps) onto Canvas.";
                    return;
                }

                // Single tweet parse
                var (singleAuthor, singleHandle, singleContent, singleTags) = ParseSocialMetadataHelper(text);
                if (CanvasThreadNodes.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(singleAuthor) && singleAuthor != "Offline User") CanvasDefaultAuthor = singleAuthor;
                    if (!string.IsNullOrWhiteSpace(singleHandle) && singleHandle != "@offline") CanvasDefaultHandle = singleHandle;
                    CanvasThreadTitle = SuggestTitle(singleContent);
                }

                var singleNode = new ThreadNode
                {
                    StepNumber = CanvasThreadNodes.Count + 1,
                    AuthorName = string.IsNullOrWhiteSpace(singleAuthor) || singleAuthor == "Offline User" ? CanvasDefaultAuthor : singleAuthor,
                    AuthorHandle = string.IsNullOrWhiteSpace(singleHandle) || singleHandle == "@offline" ? CanvasDefaultHandle : singleHandle,
                    Content = singleContent,
                    Tags = string.IsNullOrWhiteSpace(singleTags) ? CanvasDefaultTags : singleTags,
                    AttachedImagePaths = new ObservableCollection<string>(attachedMedia)
                };
                singleNode.NotifyMediaChanged();
                CanvasThreadNodes.Add(singleNode);
                StatusMessage = $"Pasted tweet as Step #{singleNode.StepNumber} on Canvas.";
            }
            else if (attachedMedia.Count > 0)
            {
                if (CanvasThreadNodes.Count > 0)
                {
                    var last = CanvasThreadNodes[CanvasThreadNodes.Count - 1];
                    foreach (var m in attachedMedia)
                    {
                        last.AttachedImagePaths.Add(m);
                    }
                    last.NotifyMediaChanged();
                    StatusMessage = $"Attached {attachedMedia.Count} media item(s) to Step #{last.StepNumber}.";
                }
                else
                {
                    var node = new ThreadNode
                    {
                        StepNumber = 1,
                        AuthorName = CanvasDefaultAuthor,
                        AuthorHandle = CanvasDefaultHandle,
                        Content = "Thread Media Attachment",
                        Tags = CanvasDefaultTags,
                        AttachedImagePaths = new ObservableCollection<string>(attachedMedia)
                    };
                    node.NotifyMediaChanged();
                    CanvasThreadNodes.Add(node);
                    StatusMessage = "Created Step #1 with pasted image/media.";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Paste Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ArchiveThreadAsSeriesAsync()
    {
        if (CanvasThreadNodes.Count == 0)
        {
            MessageBox.Show("Thread Canvas is empty. Add or paste some tweet steps first!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsLoading = true;
        StatusMessage = "Archiving thread series into Windows ESE Database...";

        try
        {
            string threadId = Guid.NewGuid().ToString("N");
            string targetFolder = string.IsNullOrWhiteSpace(CanvasSelectedFolder) ? "Coding" : CanvasSelectedFolder;
            DateTime baseDate = DateTime.Now.AddMinutes(-CanvasThreadNodes.Count * 2);

            var createdPosts = new List<Post>();

            for (int i = 0; i < CanvasThreadNodes.Count; i++)
            {
                var node = CanvasThreadNodes[i];
                var post = await _clipboardService.CreateManualPostAsync(
                    string.IsNullOrWhiteSpace(node.AuthorName) ? CanvasDefaultAuthor : node.AuthorName,
                    string.IsNullOrWhiteSpace(node.AuthorHandle) ? CanvasDefaultHandle : node.AuthorHandle,
                    node.Content,
                    targetFolder,
                    string.IsNullOrWhiteSpace(node.Tags) ? CanvasDefaultTags : node.Tags,
                    node.AttachedImagePaths
                );

                if (post != null)
                {
                    post.ThreadId = threadId;
                    post.ThreadIndex = node.StepNumber;
                    post.PostDate = baseDate.AddMinutes(i * 2);
                    await _dbService.UpdatePostAsync(post);
                    createdPosts.Add(post);
                }
            }

            foreach (var p in createdPosts.AsEnumerable().Reverse())
            {
                Posts.Insert(0, p);
            }

            await UpdateCountsAndSizeAsync();
            ApplyFilter();
            SelectedPost = createdPosts.FirstOrDefault();

            var choice = MessageBox.Show(
                $"Successfully archived {createdPosts.Count} steps as a connected thread series in '{targetFolder}'!\n\nWould you like to clear the Thread Canvas now?",
                "Thread Series Archived",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (choice == MessageBoxResult.Yes)
            {
                CanvasThreadNodes.Clear();
                CanvasThreadTitle = "New Twitter / X Thread";
            }

            IsCanvasViewActive = false;
            StatusMessage = $"Archived thread series with {createdPosts.Count} posts into '{targetFolder}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Thread Archiving Error: {ex.Message}";
            MessageBox.Show($"Failed to archive thread series:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task MergeAndArchiveThreadAsync()
    {
        if (CanvasThreadNodes.Count == 0)
        {
            MessageBox.Show("Thread Canvas is empty. Add or paste some tweet steps first!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsLoading = true;
        StatusMessage = "Merging and archiving thread into single post...";

        try
        {
            string targetFolder = string.IsNullOrWhiteSpace(CanvasSelectedFolder) ? "Coding" : CanvasSelectedFolder;

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(CanvasThreadTitle) && CanvasThreadTitle != "New Twitter / X Thread")
            {
                sb.AppendLine($"🧵 {CanvasThreadTitle}");
                sb.AppendLine();
            }

            for (int i = 0; i < CanvasThreadNodes.Count; i++)
            {
                var node = CanvasThreadNodes[i];
                sb.AppendLine($"[{node.StepNumber}/{CanvasThreadNodes.Count}] {node.Content}");
                if (i < CanvasThreadNodes.Count - 1)
                {
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
            }

            var allMedia = new List<string>();
            foreach (var node in CanvasThreadNodes)
            {
                foreach (var m in node.AttachedImagePaths)
                {
                    if (!allMedia.Contains(m)) allMedia.Add(m);
                }
            }

            var allTags = string.Join(" ", CanvasThreadNodes.Select(n => n.Tags).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct());
            if (string.IsNullOrWhiteSpace(allTags)) allTags = CanvasDefaultTags;

            var post = await _clipboardService.CreateManualPostAsync(
                CanvasDefaultAuthor,
                CanvasDefaultHandle,
                sb.ToString(),
                targetFolder,
                allTags,
                allMedia
            );

            if (post != null)
            {
                Posts.Insert(0, post);
                await UpdateCountsAndSizeAsync();
                ApplyFilter();
                SelectedPost = post;

                var choice = MessageBox.Show(
                    $"Successfully merged {CanvasThreadNodes.Count} tweet steps into 1 master post in '{targetFolder}'!\n\nWould you like to clear the Thread Canvas now?",
                    "Merged Thread Archived",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (choice == MessageBoxResult.Yes)
                {
                    CanvasThreadNodes.Clear();
                    CanvasThreadTitle = "New Twitter / X Thread";
                }

                IsCanvasViewActive = false;
                StatusMessage = $"Merged and archived thread into '{targetFolder}'.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Merge Error: {ex.Message}";
            MessageBox.Show($"Failed to merge thread:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ExportThreadMarkdown()
    {
        if (CanvasThreadNodes.Count == 0)
        {
            MessageBox.Show("Thread Canvas is empty. Add or paste some tweet steps first!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# {CanvasThreadTitle}");
        sb.AppendLine();
        sb.AppendLine($"- **Author:** {CanvasDefaultAuthor} ({CanvasDefaultHandle})");
        sb.AppendLine($"- **Folder:** {CanvasSelectedFolder}");
        sb.AppendLine($"- **Steps:** {CanvasThreadNodes.Count}");
        sb.AppendLine($"- **Exported:** {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var node in CanvasThreadNodes)
        {
            sb.AppendLine($"### 🧵 Step {node.StepNumber}/{CanvasThreadNodes.Count}");
            if (!string.IsNullOrWhiteSpace(node.AuthorName) && node.AuthorName != CanvasDefaultAuthor)
            {
                sb.AppendLine($"*{node.AuthorName} ({node.AuthorHandle})*");
                sb.AppendLine();
            }
            sb.AppendLine(node.Content);
            if (!string.IsNullOrWhiteSpace(node.Tags))
            {
                sb.AppendLine();
                sb.AppendLine($"*Tags: {node.Tags}*");
            }
            if (node.AttachedImagePaths.Count > 0)
            {
                sb.AppendLine();
                foreach (var m in node.AttachedImagePaths)
                {
                    var fn = Path.GetFileName(m);
                    sb.AppendLine($"- ![{fn}]({m})");
                }
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            StatusMessage = "Thread exported and copied to clipboard as GitHub Markdown!";
            MessageBox.Show("Thread exported to clipboard in GitHub Markdown format!", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export Error: {ex.Message}";
        }
    }

    private void RenumberCanvasNodes()
    {
        for (int i = 0; i < CanvasThreadNodes.Count; i++)
        {
            CanvasThreadNodes[i].StepNumber = i + 1;
        }
    }

    private static string SuggestTitle(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "New Twitter / X Thread";
        var firstLine = content.Trim().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstLine))
        {
            if (firstLine.Length > 60) return firstLine.Substring(0, 57) + "...";
            return firstLine;
        }
        return "New Twitter / X Thread";
    }

    private static (string authorName, string authorHandle, string content, string tags) ParseSocialMetadataHelper(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return ("Offline User", "@offline", string.Empty, string.Empty);
        }

        rawText = rawText.Trim();
        var lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string authorName = "Offline User";
        string authorHandle = "@offline";
        string content = rawText;

        if (lines.Length >= 2 && lines[1].Trim().StartsWith("@"))
        {
            authorName = lines[0].Trim();
            var handleMatch = Regex.Match(lines[1].Trim(), @"(@[A-Za-z0-9_]+)");
            if (handleMatch.Success) authorHandle = handleMatch.Value;
            content = string.Join(Environment.NewLine, lines.Skip(2)).Trim();
            if (string.IsNullOrWhiteSpace(content)) content = rawText;
        }
        else
        {
            var handleMatch = Regex.Match(rawText, @"(@[A-Za-z0-9_]+)");
            if (handleMatch.Success)
            {
                authorHandle = handleMatch.Value;
                var clean = authorHandle.TrimStart('@');
                authorName = char.ToUpperInvariant(clean[0]) + (clean.Length > 1 ? clean.Substring(1) : "");
            }
        }

        var tagMatches = Regex.Matches(rawText, @"#([a-zA-Z0-9_\-]+)");
        var tags = string.Join(" ", tagMatches.Select(m => m.Value));

        return (authorName, authorHandle, content, tags);
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

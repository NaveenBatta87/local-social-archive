using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;
using LocalSocialArchive.Services;
using LocalSocialArchive.Views;

namespace LocalSocialArchive.ViewModels;

public partial class StoryCanvasViewModel : ObservableObject
{
    private readonly IEseDatabaseService _dbService;
    private readonly string _mediaBaseDir;

    [ObservableProperty]
    private string _documentTitle = "Untitled Story Document";

    [ObservableProperty]
    private string _authorName = "Author";

    [ObservableProperty]
    private string _authorHandle = "@author";

    [ObservableProperty]
    private string _folderName = "Threads";

    [ObservableProperty]
    private ObservableCollection<string> _availableFolders = new();

    [ObservableProperty]
    private ObservableCollection<CanvasDocumentPart> _parts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPreviousPart))]
    [NotifyPropertyChangedFor(nameof(CanGoNextPart))]
    [NotifyPropertyChangedFor(nameof(PartIndicatorText))]
    private int _currentPartIndex = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPreviousPart))]
    [NotifyPropertyChangedFor(nameof(CanGoNextPart))]
    [NotifyPropertyChangedFor(nameof(PartIndicatorText))]
    private int _totalParts = 1;

    [ObservableProperty]
    private CanvasDocumentPart? _currentPart;

    public bool CanGoPreviousPart => CurrentPartIndex > 1;
    public bool CanGoNextPart => CurrentPartIndex < TotalParts;
    public string PartIndicatorText => $"Part {CurrentPartIndex} of {Math.Max(1, TotalParts)}";

    [ObservableProperty]
    private double _currentPartSizeMB;

    [ObservableProperty]
    private string _sizeBadgeText = "0.00 MB / 12.0 MB";

    [ObservableProperty]
    private bool _is12MBWarningVisible;

    [ObservableProperty]
    private string _statusMessage = "Ready. Paste web content or write below — images stay inline.";

    [ObservableProperty]
    private bool _isBusy;

    public event Action? RequestClose;

    public StoryCanvasViewModel(
        IEseDatabaseService dbService,
        IEnumerable<string>? existingFolders = null,
        IEnumerable<Post>? initialPosts = null)
    {
        _dbService = dbService;
        _mediaBaseDir = Path.Combine(StoragePathHelper.MediaDirectory, "CanvasDocs");
        Directory.CreateDirectory(_mediaBaseDir);

        if (existingFolders != null)
        {
            foreach (var f in existingFolders)
            {
                if (!string.IsNullOrWhiteSpace(f) && !AvailableFolders.Contains(f))
                {
                    AvailableFolders.Add(f);
                }
            }
        }
        if (AvailableFolders.Count == 0)
        {
            AvailableFolders.Add("Threads");
            AvailableFolders.Add("Coding");
        }

        if (initialPosts != null && initialPosts.Any())
        {
            LoadFromExistingPosts(initialPosts);
        }
        else
        {
            InitializeNewDocument();
        }
    }

    private void InitializeNewDocument()
    {
        var firstPart = new CanvasDocumentPart
        {
            PartIndex = 1,
            TotalParts = 1,
            Title = DocumentTitle
        };
        firstPart.Blocks.Add(new CanvasBlock
        {
            Type = CanvasBlockType.Paragraph,
            Text = string.Empty
        });

        Parts.Clear();
        Parts.Add(firstPart);
        CurrentPart = firstPart;
        CurrentPartIndex = 1;
        TotalParts = 1;
        RecalculateSize();
    }

    private void LoadFromExistingPosts(IEnumerable<Post> posts)
    {
        var ordered = posts.OrderBy(p => p.ThreadIndex).ToList();
        if (ordered.Count == 0) return;

        var first = ordered[0];
        AuthorName = !string.IsNullOrWhiteSpace(first.AuthorName) ? first.AuthorName : "Author";
        AuthorHandle = !string.IsNullOrWhiteSpace(first.AuthorHandle) ? first.AuthorHandle : "@author";
        FolderName = first.FolderName;

        var firstLine = first.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        DocumentTitle = !string.IsNullOrWhiteSpace(firstLine) ? (firstLine.Length > 70 ? firstLine.Substring(0, 67) + "..." : firstLine) : "Archived Story";

        Parts.Clear();
        int idx = 1;
        foreach (var p in ordered)
        {
            var part = new CanvasDocumentPart
            {
                PartIndex = idx++,
                TotalParts = ordered.Count,
                Title = $"{DocumentTitle} - Part {idx - 1}"
            };

            var blocks = CanvasDocumentHelper.ParseFromContent(p.Content, p.MediaItems);
            foreach (var b in blocks)
            {
                part.Blocks.Add(b);
            }
            Parts.Add(part);
        }

        TotalParts = Parts.Count;
        CurrentPartIndex = 1;
        CurrentPart = Parts[0];
        RecalculateSize();
    }

    public void RecalculateSize()
    {
        if (CurrentPart == null) return;
        long totalBytes = CurrentPart.TotalSizeBytes;
        CurrentPartSizeMB = totalBytes / (1024.0 * 1024.0);
        SizeBadgeText = $"{CurrentPartSizeMB:F2} MB / 12.0 MB";
        Is12MBWarningVisible = totalBytes > (12L * 1024 * 1024);
    }

    [RelayCommand]
    public async Task PasteFromClipboardAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Reading clipboard and saving inline media...";

            string? html = null;
            if (Clipboard.ContainsText(TextDataFormat.Html))
            {
                html = Clipboard.GetText(TextDataFormat.Html);
            }

            string? plainText = null;
            if (Clipboard.ContainsText())
            {
                plainText = Clipboard.GetText();
            }

            if (string.IsNullOrWhiteSpace(html) && string.IsNullOrWhiteSpace(plainText) && !Clipboard.ContainsImage())
            {
                StatusMessage = "Clipboard is empty or does not contain readable content.";
                return;
            }

            // If clipboard contains a direct bitmap image
            if (Clipboard.ContainsImage() && string.IsNullOrWhiteSpace(plainText))
            {
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    string filePath = Path.Combine(_mediaBaseDir, $"canvas_pasted_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                    using (var fs = File.Create(filePath))
                    {
                        encoder.Save(fs);
                    }

                    long size = new FileInfo(filePath).Length;
                    CurrentPart?.Blocks.Add(new CanvasBlock
                    {
                        Type = CanvasBlockType.Image,
                        ImagePath = filePath,
                        ImageSizeBytes = size
                    });

                    RecalculateSize();
                    Check12MBLimitAndSuggest();
                    StatusMessage = "Pasted image inline.";
                    return;
                }
            }

            // Otherwise parse sequential text and images
            List<CanvasBlock> parsedBlocks;
            if (!string.IsNullOrWhiteSpace(html))
            {
                parsedBlocks = await CanvasDocumentHelper.ParseHtmlOrTextWithInlineImagesAsync(html, _mediaBaseDir);
            }
            else
            {
                parsedBlocks = CanvasDocumentHelper.ParseFromContent(plainText!);
            }

            if (parsedBlocks.Count > 0 && CurrentPart != null)
            {
                // If the only block in current part is the default placeholder, remove it
                if (CurrentPart.Blocks.Count == 1 && CurrentPart.Blocks[0].IsParagraph && CurrentPart.Blocks[0].Text.StartsWith("Type or paste"))
                {
                    CurrentPart.Blocks.Clear();
                }

                foreach (var b in parsedBlocks)
                {
                    CurrentPart.Blocks.Add(b);
                }

                RecalculateSize();
                StatusMessage = $"Pasted {parsedBlocks.Count} items. All inline images preserved in position.";
                Check12MBLimitAndSuggest();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Paste Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void InsertImageFile()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Insert Image into Document Canvas",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.gif|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (ofd.ShowDialog() == true && CurrentPart != null)
        {
            foreach (var fn in ofd.FileNames)
            {
                if (File.Exists(fn))
                {
                    string ext = Path.GetExtension(fn);
                    string target = Path.Combine(_mediaBaseDir, $"doc_img_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}{ext}");
                    File.Copy(fn, target, true);

                    long size = new FileInfo(target).Length;
                    CurrentPart.Blocks.Add(new CanvasBlock
                    {
                        Type = CanvasBlockType.Image,
                        ImagePath = target,
                        ImageSizeBytes = size
                    });
                }
            }

            RecalculateSize();
            StatusMessage = "Inserted image inline.";
            Check12MBLimitAndSuggest();
        }
    }

    [RelayCommand]
    public void AddParagraphBlock()
    {
        if (CurrentPart == null) return;
        var newBlock = new CanvasBlock
        {
            Type = CanvasBlockType.Paragraph,
            Text = string.Empty
        };
        CurrentPart.Blocks.Add(newBlock);
    }

    [RelayCommand]
    public void DeleteBlock(CanvasBlock? block)
    {
        if (block == null || CurrentPart == null) return;
        CurrentPart.Blocks.Remove(block);
        RecalculateSize();
        StatusMessage = "Removed block.";
    }

    [RelayCommand]
    public void CleanTextStyling()
    {
        if (CurrentPart == null) return;
        CanvasDocumentHelper.CleanTextStyling(CurrentPart.Blocks);
        RecalculateSize();
        StatusMessage = "🧹 Cleaned text styling. Web noise stripped; all inline images preserved.";
    }

    private void Check12MBLimitAndSuggest()
    {
        if (CurrentPart == null) return;
        if (CurrentPart.Exceeds12MB)
        {
            var res = MessageBox.Show(
                $"This document page currently has {CurrentPartSizeMB:F1} MB of content and images, exceeding the 12.0 MB recommended page size limit.\n\n" +
                "Would you like to automatically split the overflowing content into Part 2 of this story document?",
                "Document Size Exceeds 12 MB",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                SplitIntoNewPart();
            }
        }
    }

    [RelayCommand]
    public void SplitIntoNewPart()
    {
        if (CurrentPart == null || CurrentPart.Blocks.Count < 2) return;

        // Find split point: take blocks from the bottom until current part is <= 10MB
        var movingBlocks = new List<CanvasBlock>();
        long currentBytes = CurrentPart.TotalSizeBytes;
        long targetBytes = 10L * 1024 * 1024; // 10 MB

        for (int i = CurrentPart.Blocks.Count - 1; i >= 1; i--)
        {
            var b = CurrentPart.Blocks[i];
            movingBlocks.Insert(0, b);
            CurrentPart.Blocks.RemoveAt(i);
            currentBytes -= (b.IsImage ? b.ImageSizeBytes : (long)b.Text.Length);
            if (currentBytes <= targetBytes) break;
        }

        int newPartIdx = CurrentPartIndex + 1;
        var nextPart = new CanvasDocumentPart
        {
            PartIndex = newPartIdx,
            TotalParts = TotalParts + 1,
            Title = $"{DocumentTitle} - Part {newPartIdx}"
        };

        foreach (var b in movingBlocks)
        {
            nextPart.Blocks.Add(b);
        }

        Parts.Insert(CurrentPartIndex, nextPart);
        // Renumber all parts
        for (int i = 0; i < Parts.Count; i++)
        {
            Parts[i].PartIndex = i + 1;
            Parts[i].TotalParts = Parts.Count;
        }

        TotalParts = Parts.Count;
        CurrentPartIndex = newPartIdx;
        CurrentPart = nextPart;

        RecalculateSize();
        StatusMessage = $"Split into Part {newPartIdx}. You are now editing Part {newPartIdx}.";
    }

    [RelayCommand]
    public void CreateNewPart()
    {
        int newIdx = Parts.Count + 1;
        var part = new CanvasDocumentPart
        {
            PartIndex = newIdx,
            TotalParts = newIdx,
            Title = $"{DocumentTitle} - Part {newIdx}"
        };
        part.Blocks.Add(new CanvasBlock { Type = CanvasBlockType.Paragraph, Text = string.Empty });
        Parts.Add(part);

        foreach (var p in Parts)
        {
            p.TotalParts = Parts.Count;
        }
        TotalParts = Parts.Count;
        CurrentPartIndex = newIdx;
        CurrentPart = part;
        RecalculateSize();
        StatusMessage = $"Added new empty Part {newIdx}.";
    }

    [RelayCommand]
    public void NextPart()
    {
        if (CurrentPartIndex < TotalParts)
        {
            CurrentPartIndex++;
            CurrentPart = Parts[CurrentPartIndex - 1];
            RecalculateSize();
        }
    }

    [RelayCommand]
    public void PreviousPart()
    {
        if (CurrentPartIndex > 1)
        {
            CurrentPartIndex--;
            CurrentPart = Parts[CurrentPartIndex - 1];
            RecalculateSize();
        }
    }

    [RelayCommand]
    public async Task SaveStoryDocumentAsync()
    {
        if (Parts.Count == 0 || (Parts.Count == 1 && Parts[0].Blocks.Count == 0))
        {
            MessageBox.Show("Please add some content before saving.", "Empty Document", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Saving story document parts to offline database...";

            string threadId = Guid.NewGuid().ToString("N");
            var postsToInsert = new List<Post>();
            DateTime baseTime = DateTime.Now.AddMinutes(-Parts.Count);

            for (int i = 0; i < Parts.Count; i++)
            {
                var part = Parts[i];
                string postId = Guid.NewGuid().ToString("N");
                string markdown = CanvasDocumentHelper.SerializeToMarkdown(part.Blocks);

                var post = new Post
                {
                    Id = postId,
                    AuthorName = string.IsNullOrWhiteSpace(AuthorName) ? "Author" : AuthorName.Trim(),
                    AuthorHandle = string.IsNullOrWhiteSpace(AuthorHandle) ? "@author" : (AuthorHandle.StartsWith('@') ? AuthorHandle.Trim() : "@" + AuthorHandle.Trim()),
                    Content = markdown,
                    PostDate = baseTime.AddMinutes(i),
                    FolderName = string.IsNullOrWhiteSpace(FolderName) ? "Threads" : FolderName.Trim(),
                    Tags = "#story #canvas",
                    FolderPath = _mediaBaseDir,
                    CreatedAt = DateTime.Now,
                    ThreadId = threadId,
                    ThreadIndex = i + 1
                };

                // Add media items for any inline images so media browser indexes them
                foreach (var b in part.Blocks.Where(x => x.IsImage && !string.IsNullOrEmpty(x.ImagePath)))
                {
                    post.MediaItems.Add(new MediaItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        PostId = postId,
                        FilePath = b.ImagePath!,
                        MediaType = "Image",
                        CreatedAt = DateTime.Now
                    });
                }

                postsToInsert.Add(post);
            }

            await _dbService.InsertPostsBatchAsync(postsToInsert);
            StatusMessage = $"Saved {postsToInsert.Count} document part(s) to archive!";

            var openAsk = MessageBox.Show(
                $"Successfully saved {postsToInsert.Count} story part(s) to folder '{FolderName}'!\n\nWould you like to open this story document in the Document Reader now?",
                "Story Document Saved",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (openAsk == MessageBoxResult.Yes)
            {
                OpenReader(postsToInsert);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save Error: {ex.Message}";
            MessageBox.Show($"Failed to save document:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void OpenReader(IEnumerable<Post>? posts = null)
    {
        var targetPosts = posts?.ToList();
        if (targetPosts == null || targetPosts.Count == 0)
        {
            // Build temporary post items from current in-memory parts
            targetPosts = new List<Post>();
            string tempThreadId = Guid.NewGuid().ToString("N");
            for (int i = 0; i < Parts.Count; i++)
            {
                var p = Parts[i];
                var post = new Post
                {
                    Id = Guid.NewGuid().ToString("N"),
                    AuthorName = AuthorName,
                    AuthorHandle = AuthorHandle,
                    Content = CanvasDocumentHelper.SerializeToMarkdown(p.Blocks),
                    PostDate = DateTime.Now,
                    ThreadId = tempThreadId,
                    ThreadIndex = i + 1
                };
                foreach (var b in p.Blocks.Where(x => x.IsImage && !string.IsNullOrEmpty(x.ImagePath)))
                {
                    post.MediaItems.Add(new MediaItem { FilePath = b.ImagePath! });
                }
                targetPosts.Add(post);
            }
        }

        var readerVm = new StoryDocumentReaderViewModel(targetPosts);
        var readerWin = new StoryDocumentReaderWindow(readerVm);
        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
        {
            readerWin.Owner = Application.Current.MainWindow;
        }
        readerWin.Show();
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke();
    }
}

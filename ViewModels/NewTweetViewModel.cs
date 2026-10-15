using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace LocalSocialArchive.ViewModels;

public partial class NewTweetViewModel : ObservableObject
{
    [ObservableProperty]
    private string _authorName = Environment.UserName;

    [ObservableProperty]
    private string _authorHandle = "@" + Environment.UserName.ToLowerInvariant().Replace(" ", "");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharacterCount))]
    private string _content = string.Empty;

    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private string _folderName = "Coding";

    [ObservableProperty]
    private string _tags = string.Empty;

    [ObservableProperty]
    private bool _isCustomAuthorExpanded;

    [ObservableProperty]
    private bool _isTagsExpanded;

    [ObservableProperty]
    private bool _isNotesExpanded;

    [ObservableProperty]
    private ObservableCollection<string> _attachedImagePaths = new();

    public int CharacterCount => Content?.Length ?? 0;

    public bool HasAttachedMedia => AttachedImagePaths.Count > 0;

    public ObservableCollection<string> AvailableFolders { get; } = new();

    public event Action<bool>? RequestClose;

    public NewTweetViewModel(string defaultFolder = "Coding", IEnumerable<string>? folderOptions = null)
    {
        FolderName = defaultFolder;

        if (folderOptions != null)
        {
            foreach (var f in folderOptions)
            {
                if (!string.IsNullOrWhiteSpace(f) && f != "All Posts" && !AvailableFolders.Contains(f))
                {
                    AvailableFolders.Add(f);
                }
            }
        }

        if (AvailableFolders.Count == 0)
        {
            AvailableFolders.Add("Coding");
            AvailableFolders.Add("SAP Projects");
            AvailableFolders.Add("Memes");
            AvailableFolders.Add("Reference");
        }

        if (!AvailableFolders.Contains(FolderName) && AvailableFolders.Count > 0)
        {
            FolderName = AvailableFolders[0];
        }

        AttachedImagePaths.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasAttachedMedia));
        };
    }

    [RelayCommand]
    private void ToggleCustomAuthor()
    {
        IsCustomAuthorExpanded = !IsCustomAuthorExpanded;
    }

    [RelayCommand]
    private void ToggleTags()
    {
        IsTagsExpanded = !IsTagsExpanded;
    }

    [RelayCommand]
    private void ToggleNotes()
    {
        IsNotesExpanded = !IsNotesExpanded;
    }

    [RelayCommand]
    private void AddImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Media Attachments (Images, GIFs & Videos)",
            Filter = "All Supported Media|*.png;*.jpg;*.jpeg;*.jfif;*.jif;*.bmp;*.webp;*.gif;*.mp4;*.webm;*.mov;*.mkv;*.avi;*.wmv|Image Files (*.png, *.jpg, *.jfif, *.webp, *.gif)|*.png;*.jpg;*.jpeg;*.jfif;*.jif;*.bmp;*.webp;*.gif|Video Files (*.mp4, *.webm, *.mov)|*.mp4;*.webm;*.mov;*.mkv;*.avi;*.wmv|All Files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                if (!AttachedImagePaths.Contains(file))
                {
                    AttachedImagePaths.Add(file);
                }
            }
        }
    }

    public void AddDroppedFiles(IEnumerable<string> filePaths)
    {
        var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".jfif", ".jif", ".bmp", ".webp", ".gif", ".mp4", ".webm", ".mov", ".mkv"
        };

        foreach (var file in filePaths)
        {
            if (File.Exists(file) && supportedExts.Contains(Path.GetExtension(file)))
            {
                if (!AttachedImagePaths.Contains(file))
                {
                    AttachedImagePaths.Add(file);
                }
            }
        }
    }

    [RelayCommand]
    private void RemoveImage(string? path)
    {
        if (path != null)
        {
            AttachedImagePaths.Remove(path);
        }
    }

    [RelayCommand]
    private void InsertEmoji(string? emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return;
        Content = (Content ?? string.Empty) + emoji;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Content) && AttachedImagePaths.Count == 0)
        {
            System.Windows.MessageBox.Show("Please write your post or attach media to archive.", "Empty Post", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        // Auto-extract hashtags from text if tags was left empty
        if (string.IsNullOrWhiteSpace(Tags) && !string.IsNullOrWhiteSpace(Content))
        {
            var matches = Regex.Matches(Content, @"#\w+");
            if (matches.Count > 0)
            {
                var distinctTags = matches.Select(m => m.Value).Distinct();
                Tags = string.Join(" ", distinctTags);
            }
        }

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}

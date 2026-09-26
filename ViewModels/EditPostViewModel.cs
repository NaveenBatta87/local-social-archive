using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;

namespace LocalSocialArchive.ViewModels;

public partial class EditPostViewModel : ObservableObject
{
    private readonly Post _originalPost;

    [ObservableProperty]
    private string _authorName;

    [ObservableProperty]
    private string _authorHandle;

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private string _comment;

    [ObservableProperty]
    private string _folderName;

    [ObservableProperty]
    private string _tags;

    [ObservableProperty]
    private ObservableCollection<string> _attachedImagePaths = new();

    public ObservableCollection<string> AvailableFolders { get; } = new();

    public event Action<bool>? RequestClose;

    public EditPostViewModel(Post post, IEnumerable<string>? folderOptions = null)
    {
        _originalPost = post;
        _authorName = post.AuthorName;
        _authorHandle = post.AuthorHandle;
        _content = post.Content;
        _comment = post.Comment ?? string.Empty;
        _folderName = post.FolderName;
        _tags = post.Tags;

        foreach (var m in post.MediaItems)
        {
            if (!string.IsNullOrWhiteSpace(m.FilePath))
            {
                _attachedImagePaths.Add(m.FilePath);
            }
        }

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

        if (!AvailableFolders.Contains(FolderName))
        {
            AvailableFolders.Add(FolderName);
        }
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
    private async Task SaveAsync()
    {
        _originalPost.AuthorName = AuthorName;
        _originalPost.AuthorHandle = AuthorHandle;
        _originalPost.Content = Content;
        _originalPost.Comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();
        _originalPost.FolderName = FolderName;
        _originalPost.Tags = Tags;

        // Synchronize media items
        var targetDir = _originalPost.FolderPath;
        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
        {
            string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string baseDir = Path.Combine(userDocs, "LocalSocialArchive", "Media");
            targetDir = Path.Combine(baseDir, $"{DateTime.Now:yyyyMMdd}_{_originalPost.Id.Substring(0, 8)}");
            Directory.CreateDirectory(targetDir);
            _originalPost.FolderPath = targetDir;
        }

        var newMediaCollection = new ObservableCollection<MediaItem>();
        int mediaIndex = 1;

        foreach (var path in AttachedImagePaths)
        {
            if (!File.Exists(path)) continue;

            // Check if already in MediaItems
            var existing = _originalPost.MediaItems.FirstOrDefault(m => string.Equals(m.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                newMediaCollection.Add(existing);
            }
            else
            {
                // New file added during edit - copy to post folder
                var ext = Path.GetExtension(path);
                bool isVid = MediaItem.IsVideoExtension(path);
                string fileName = $"{(isVid ? "video" : "media")}_{mediaIndex++}{ext}";
                string dest = Path.Combine(targetDir, fileName);

                if (!string.Equals(path, dest, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(path, dest, true);
                }

                string? thumbPath = null;
                if (isVid)
                {
                    thumbPath = Path.Combine(targetDir, $"{Path.GetFileNameWithoutExtension(fileName)}_thumb.png");
                    await ThumbnailHelper.GenerateVideoThumbnailAsync(dest, thumbPath);
                    if (!File.Exists(thumbPath)) thumbPath = null;
                }

                newMediaCollection.Add(new MediaItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PostId = _originalPost.Id,
                    FilePath = dest,
                    ThumbnailPath = thumbPath,
                    MediaType = isVid ? "Video" : "Image",
                    CreatedAt = DateTime.Now
                });
            }
        }

        _originalPost.MediaItems = newMediaCollection;

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}

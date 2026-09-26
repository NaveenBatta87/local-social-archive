using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace LocalSocialArchive.ViewModels;

public partial class NewTweetViewModel : ObservableObject
{
    [ObservableProperty]
    private string _authorName = "Offline User";

    [ObservableProperty]
    private string _authorHandle = "@offline";

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private string _folderName = "Coding";

    [ObservableProperty]
    private string _tags = "#csharp #windows";

    [ObservableProperty]
    private ObservableCollection<string> _attachedImagePaths = new();

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
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Content) && AttachedImagePaths.Count == 0)
        {
            System.Windows.MessageBox.Show("Please enter content or attach media.", "Validation", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}

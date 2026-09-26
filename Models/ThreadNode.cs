using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LocalSocialArchive.Models;

public partial class ThreadNode : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private int _stepNumber = 1;

    [ObservableProperty]
    private string _authorName = "Offline User";

    [ObservableProperty]
    private string _authorHandle = "@offline";

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string _tags = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _attachedImagePaths = new();

    public bool HasAttachedImages => AttachedImagePaths.Count > 0;
    public bool HasMedia => AttachedImagePaths.Count > 0;
    public int MediaCount => AttachedImagePaths.Count;
    public string? FirstMedia => AttachedImagePaths.Count > 0 ? AttachedImagePaths[0] : null;

    [RelayCommand]
    public void InsertEmoji(string emoji)
    {
        if (string.IsNullOrEmpty(Content))
        {
            Content = emoji;
        }
        else
        {
            Content += " " + emoji;
        }
    }

    public void NotifyMediaChanged()
    {
        OnPropertyChanged(nameof(HasAttachedImages));
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(MediaCount));
        OnPropertyChanged(nameof(FirstMedia));
    }
}

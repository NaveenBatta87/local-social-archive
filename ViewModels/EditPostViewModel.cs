using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private string _folderName;

    [ObservableProperty]
    private string _tags;

    public ObservableCollection<string> AvailableFolders { get; } = new();

    public event Action<bool>? RequestClose;

    public EditPostViewModel(Post post, IEnumerable<string>? folderOptions = null)
    {
        _originalPost = post;
        _authorName = post.AuthorName;
        _authorHandle = post.AuthorHandle;
        _content = post.Content;
        _folderName = post.FolderName;
        _tags = post.Tags;

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
    private void Save()
    {
        _originalPost.AuthorName = AuthorName;
        _originalPost.AuthorHandle = AuthorHandle;
        _originalPost.Content = Content;
        _originalPost.FolderName = FolderName;
        _originalPost.Tags = Tags;

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}

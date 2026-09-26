using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalSocialArchive.Models;

public partial class CollectionGroup : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _iconGlyph = "🗂️";

    [ObservableProperty]
    private string _description = "Collection of archived folders";

    [ObservableProperty]
    private string _accentColor = "#1D9BF0";

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isDeletable = true;

    [ObservableProperty]
    private ObservableCollection<FolderCategory> _folders = new();

    public int FolderCount => Folders.Count;

    public int TotalPostsCount => Folders.Sum(f => f.Count);

    public void NotifyHierarchyChanged()
    {
        OnPropertyChanged(nameof(FolderCount));
        OnPropertyChanged(nameof(TotalPostsCount));
    }
}

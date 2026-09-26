using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalSocialArchive.Models;

public partial class FolderCategory : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _collectionName = "General Archive";

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private string _iconGlyph = "📁";

    [ObservableProperty]
    private string _description = "Offline categorized archive";

    [ObservableProperty]
    private string _accentColor = "#1D9BF0";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ObservableCollection<string> _recentThumbnails = new();

    [ObservableProperty]
    private string _lastUpdatedText = "Active";

    public bool HasThumbnails => RecentThumbnails.Count > 0;
    public string? FirstThumbnail => RecentThumbnails.Count > 0 ? RecentThumbnails[0] : null;
    public string? SecondThumbnail => RecentThumbnails.Count > 1 ? RecentThumbnails[1] : null;
    public string? ThirdThumbnail => RecentThumbnails.Count > 2 ? RecentThumbnails[2] : null;

    public bool IsDeletable => !string.Equals(Name, "All Posts", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(Name, "Coding", StringComparison.OrdinalIgnoreCase);
}

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
    [NotifyPropertyChangedFor(nameof(HasThumbnails))]
    [NotifyPropertyChangedFor(nameof(FirstThumbnail))]
    [NotifyPropertyChangedFor(nameof(SecondThumbnail))]
    [NotifyPropertyChangedFor(nameof(ThirdThumbnail))]
    [NotifyPropertyChangedFor(nameof(HasSingleThumbnail))]
    [NotifyPropertyChangedFor(nameof(HasMultipleThumbnails))]
    private ObservableCollection<string> _recentThumbnails = new();

    [ObservableProperty]
    private string _lastUpdatedText = "Active";

    public bool HasThumbnails => RecentThumbnails.Count > 0;
    public bool HasSingleThumbnail => RecentThumbnails.Count == 1;
    public bool HasMultipleThumbnails => RecentThumbnails.Count > 1;
    public string? FirstThumbnail => RecentThumbnails.Count > 0 ? RecentThumbnails[0] : null;
    public string? SecondThumbnail => RecentThumbnails.Count > 1 ? RecentThumbnails[1] : null;
    public string? ThirdThumbnail => RecentThumbnails.Count > 2 ? RecentThumbnails[2] : null;

    public string DisplayGlyph
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(IconGlyph) && IconGlyph != "📁") return IconGlyph;
            var lower = Name.ToLowerInvariant();
            if (lower.Contains("code") || lower.Contains("coding") || lower.Contains("dev")) return "💻";
            if (lower.Contains("meme")) return "🎭";
            if (lower.Contains("ref") || lower.Contains("book") || lower.Contains("read")) return "📚";
            if (lower.Contains("sap") || lower.Contains("work") || lower.Contains("job") || lower.Contains("biz")) return "💼";
            if (lower.Contains("art") || lower.Contains("design") || lower.Contains("photo")) return "🎨";
            if (lower.Contains("tech") || lower.Contains("ai")) return "🤖";
            if (lower.Contains("crypto") || lower.Contains("finance")) return "📈";
            return "📁";
        }
    }

    public string DisplayAccentColor
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AccentColor) && AccentColor != "#1D9BF0") return AccentColor;
            var lower = Name.ToLowerInvariant();
            if (lower.Contains("code") || lower.Contains("coding")) return "#38BDF8";
            if (lower.Contains("meme")) return "#EC4899";
            if (lower.Contains("ref") || lower.Contains("book")) return "#F59E0B";
            if (lower.Contains("sap") || lower.Contains("work")) return "#10B981";
            return "#1D9BF0";
        }
    }

    public bool IsDeletable => !string.Equals(Name, "All Posts", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(Name, "Coding", StringComparison.OrdinalIgnoreCase);
}

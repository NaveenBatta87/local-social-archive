using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalSocialArchive.Models;

public class StoryTheme
{
    public string Name { get; set; } = "Dark Slate";
    public string BackgroundHex { get; set; } = "#0D1117";
    public string? GradientEndHex { get; set; } = "#161B22";
    public string CardBackgroundHex { get; set; } = "#161B22";
    public string ForegroundHex { get; set; } = "#F0F6FC";
    public string SecondaryForegroundHex { get; set; } = "#8B949E";
    public string AccentHex { get; set; } = "#38BDF8";
    public string BorderHex { get; set; } = "#30363D";
    public bool IsDark { get; set; } = true;

    public static List<StoryTheme> GetDefaultThemes()
    {
        return new List<StoryTheme>
        {
            new StoryTheme
            {
                Name = "Dark Slate",
                BackgroundHex = "#0D1117",
                GradientEndHex = "#161B22",
                CardBackgroundHex = "#161B22",
                ForegroundHex = "#F0F6FC",
                SecondaryForegroundHex = "#8B949E",
                AccentHex = "#38BDF8",
                BorderHex = "#30363D",
                IsDark = true
            },
            new StoryTheme
            {
                Name = "Midnight AMOLED",
                BackgroundHex = "#000000",
                GradientEndHex = "#0A0A0A",
                CardBackgroundHex = "#111111",
                ForegroundHex = "#FFFFFF",
                SecondaryForegroundHex = "#71767B",
                AccentHex = "#1D9BF0",
                BorderHex = "#222222",
                IsDark = true
            },
            new StoryTheme
            {
                Name = "Sunset Glow",
                BackgroundHex = "#1A0B2E",
                GradientEndHex = "#3F1648",
                CardBackgroundHex = "#281236",
                ForegroundHex = "#FFFFFF",
                SecondaryForegroundHex = "#E4B8ED",
                AccentHex = "#FF6584",
                BorderHex = "#53256A",
                IsDark = true
            },
            new StoryTheme
            {
                Name = "Sepia Reader",
                BackgroundHex = "#F7EFE1",
                GradientEndHex = "#EFE3CD",
                CardBackgroundHex = "#F3E8D3",
                ForegroundHex = "#2B2118",
                SecondaryForegroundHex = "#7A6855",
                AccentHex = "#9E582A",
                BorderHex = "#D8C5A8",
                IsDark = false
            },
            new StoryTheme
            {
                Name = "Modern Light",
                BackgroundHex = "#F8FAFC",
                GradientEndHex = "#EDF2F7",
                CardBackgroundHex = "#FFFFFF",
                ForegroundHex = "#0F172A",
                SecondaryForegroundHex = "#64748B",
                AccentHex = "#0284C7",
                BorderHex = "#E2E8F0",
                IsDark = false
            }
        };
    }
}

public partial class StoryItem : ObservableObject
{
    [ObservableProperty]
    private int _segmentIndex;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string? _mediaPath;

    [ObservableProperty]
    private bool _includeMedia = true;

    public bool HasMedia => !string.IsNullOrEmpty(MediaPath) && File.Exists(MediaPath);
}

public partial class StoryCard : ObservableObject
{
    [ObservableProperty]
    private int _cardIndex = 1;

    [ObservableProperty]
    private int _totalCards = 1;

    [ObservableProperty]
    private string _authorName = "Author";

    [ObservableProperty]
    private string _authorHandle = "@author";

    [ObservableProperty]
    private string _avatarInitial = "A";

    [ObservableProperty]
    private string _postDateText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<StoryItem> _items = new();

    [ObservableProperty]
    private StoryTheme _theme = new();

    [ObservableProperty]
    private double _fontSize = 38.0;

    public string CardIndexBadge => $"{CardIndex} / {TotalCards}";
}

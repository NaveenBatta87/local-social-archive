using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalSocialArchive.Models;

public partial class ThreadSegmentItem : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharacterCount))]
    private string _content = string.Empty;

    public int CharacterCount => Content?.Length ?? 0;

    [ObservableProperty]
    private ObservableCollection<string> _mediaPaths = new();

    public bool HasMedia => MediaPaths.Count > 0;

    [ObservableProperty]
    private bool _isExcluded;

    public string BadgeText => $"Tweet {Index}";
}

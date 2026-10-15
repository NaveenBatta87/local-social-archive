using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;

namespace LocalSocialArchive.ViewModels;

public partial class StoryDocumentReaderViewModel : ObservableObject
{
    [ObservableProperty]
    private string _documentTitle = "Story Document";

    [ObservableProperty]
    private string _authorName = "Author";

    [ObservableProperty]
    private string _authorHandle = "@author";

    [ObservableProperty]
    private string _avatarInitial = "A";

    [ObservableProperty]
    private string _postDateText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CanvasDocumentPart> _parts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPart))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPart))]
    [NotifyPropertyChangedFor(nameof(NextPartButtonText))]
    [NotifyPropertyChangedFor(nameof(PartBadgeText))]
    private int _currentPartIndex = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextPart))]
    [NotifyPropertyChangedFor(nameof(HasPreviousPart))]
    [NotifyPropertyChangedFor(nameof(NextPartButtonText))]
    [NotifyPropertyChangedFor(nameof(PartBadgeText))]
    private int _totalParts = 1;

    [ObservableProperty]
    private CanvasDocumentPart? _currentPart;

    public bool HasNextPart => CurrentPartIndex < TotalParts;
    public bool HasPreviousPart => CurrentPartIndex > 1;
    public string NextPartButtonText => $"Continue to Part {CurrentPartIndex + 1} ▶";
    public string PartBadgeText => $"Part {CurrentPartIndex} of {Math.Max(1, TotalParts)}";

    [ObservableProperty]
    private double _readerFontSize = 16.5;

    public event Action? RequestScrollToTop;

    public StoryDocumentReaderViewModel(IEnumerable<Post> posts)
    {
        var ordered = posts.OrderBy(p => p.ThreadIndex).ToList();
        if (ordered.Count == 0) return;

        var first = ordered[0];
        AuthorName = !string.IsNullOrWhiteSpace(first.AuthorName) ? first.AuthorName : "Author";
        AuthorHandle = !string.IsNullOrWhiteSpace(first.AuthorHandle) ? first.AuthorHandle : "@author";
        AvatarInitial = first.AvatarInitial;
        PostDateText = first.PostDate.ToString("MMMM dd, yyyy • h:mm tt");

        var firstLine = first.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstLine) && !firstLine.StartsWith("!["))
        {
            DocumentTitle = firstLine.Length > 80 ? firstLine.Substring(0, 77) + "..." : firstLine;
        }
        else
        {
            DocumentTitle = $"Story by {AuthorName}";
        }

        int idx = 1;
        foreach (var p in ordered)
        {
            var part = new CanvasDocumentPart
            {
                PartIndex = idx++,
                TotalParts = ordered.Count,
                Title = $"{DocumentTitle} (Part {idx - 1})"
            };

            var blocks = CanvasDocumentHelper.ParseFromContent(p.Content, p.MediaItems);
            foreach (var b in blocks)
            {
                part.Blocks.Add(b);
            }
            Parts.Add(part);
        }

        TotalParts = Parts.Count;
        CurrentPartIndex = 1;
        CurrentPart = Parts[0];
    }

    [RelayCommand]
    public void NextPart()
    {
        if (CurrentPartIndex < TotalParts)
        {
            CurrentPartIndex++;
            CurrentPart = Parts[CurrentPartIndex - 1];
            RequestScrollToTop?.Invoke();
        }
    }

    [RelayCommand]
    public void PreviousPart()
    {
        if (CurrentPartIndex > 1)
        {
            CurrentPartIndex--;
            CurrentPart = Parts[CurrentPartIndex - 1];
            RequestScrollToTop?.Invoke();
        }
    }

    [RelayCommand]
    public void JumpToPart(object? parameter)
    {
        int index = 1;
        if (parameter is int i)
        {
            index = i;
        }
        else if (parameter is string s && int.TryParse(s, out int parsed))
        {
            index = parsed;
        }

        if (index >= 1 && index <= TotalParts)
        {
            CurrentPartIndex = index;
            CurrentPart = Parts[index - 1];
            RequestScrollToTop?.Invoke();
        }
    }

    public event Action? RequestClose;

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke();
    }

    [RelayCommand]
    public void JumpToFirstPart()
    {
        JumpToPart(1);
    }

    [RelayCommand]
    public void ZoomInFont()
    {
        ReaderFontSize = Math.Min(24.0, ReaderFontSize + 1.5);
    }

    [RelayCommand]
    public void ZoomOutFont()
    {
        ReaderFontSize = Math.Max(13.0, ReaderFontSize - 1.5);
    }
}

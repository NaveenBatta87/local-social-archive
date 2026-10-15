using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalSocialArchive.Models;

public partial class PostComment : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _postId = string.Empty;

    [ObservableProperty]
    private string _authorName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayHandle))]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(AvatarInitial))]
    [NotifyPropertyChangedFor(nameof(AvatarBackground))]
    private string _authorHandle = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTime))]
    private string _timeAgo = string.Empty;

    [ObservableProperty]
    private DateTime _createdAt = DateTime.Now;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLikes))]
    private string _likeCount = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReplies))]
    private string _replyCount = string.Empty;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isHearted;

    public string DisplayHandle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AuthorHandle))
            {
                return AuthorHandle.StartsWith("@") ? AuthorHandle : "@" + AuthorHandle;
            }
            if (!string.IsNullOrWhiteSpace(AuthorName))
            {
                var clean = Regex.Replace(AuthorName, @"\s+", "");
                return "@" + clean.ToLowerInvariant();
            }
            return "@user";
        }
    }

    public string DisplayName => !string.IsNullOrWhiteSpace(AuthorName) ? AuthorName : DisplayHandle;

    public string DisplayTime
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(TimeAgo)) return TimeAgo;
            return RelativeTime;
        }
    }

    public string RelativeTime
    {
        get
        {
            var span = DateTime.Now - CreatedAt;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}w ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";
            return $"{(int)(span.TotalDays / 365)}y ago";
        }
    }

    public string AvatarInitial
    {
        get
        {
            var target = !string.IsNullOrWhiteSpace(AuthorName) ? AuthorName : AuthorHandle;
            if (string.IsNullOrWhiteSpace(target)) return "?";
            target = target.TrimStart('@').Trim();
            var parts = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            }
            return target.Length > 0 ? target.Substring(0, 1).ToUpper() : "?";
        }
    }

    public string AvatarBackground
    {
        get
        {
            // Deterministic modern avatar background colors matching social/YouTube aesthetics
            string[] colors =
            {
                "#E53935", "#D81B60", "#8E24AA", "#5E35B1",
                "#3949AB", "#1E88E5", "#039BE5", "#00ACC1",
                "#00897B", "#43A047", "#7CB342", "#C0CA33",
                "#FDD835", "#FFB300", "#FB8C00", "#F4511E"
            };
            var text = DisplayHandle ?? "default";
            uint hash = 0;
            foreach (char c in text)
            {
                hash = (hash * 31) + c;
            }
            return colors[hash % colors.Length];
        }
    }

    public bool HasLikes => !string.IsNullOrWhiteSpace(LikeCount) && LikeCount != "0";
    public bool HasReplies => !string.IsNullOrWhiteSpace(ReplyCount) && ReplyCount != "0";
}

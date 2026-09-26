using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace LocalSocialArchive.Models;

public class Post
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AuthorName { get; set; } = "Offline User";
    public string AuthorHandle { get; set; } = "@offline";
    public string Content { get; set; } = string.Empty;
    public DateTime PostDate { get; set; } = DateTime.Now;
    public string FolderName { get; set; } = "Coding";
    public string Tags { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
    
    private string _folderPath = string.Empty;
    public string FolderPath
    {
        get => ResolveFolderPath(_folderPath);
        set => _folderPath = value ?? string.Empty;
    }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? ThreadId { get; set; }
    public int ThreadIndex { get; set; } = 0;
    public bool IsPartOfThread => !string.IsNullOrWhiteSpace(ThreadId);

    private static string ResolveFolderPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
        if (System.IO.Directory.Exists(rawPath)) return rawPath;

        try
        {
            string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string baseDir = System.IO.Path.Combine(userDocs, "LocalSocialArchive");
            int idx = rawPath.IndexOf("LocalSocialArchive", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string rel = rawPath.Substring(idx + "LocalSocialArchive".Length).TrimStart('\\', '/');
                string candidate = System.IO.Path.Combine(baseDir, rel);
                if (System.IO.Directory.Exists(candidate)) return candidate;
            }
        }
        catch { }

        return rawPath;
    }

    public ObservableCollection<MediaItem> MediaItems { get; set; } = new();

    public bool HasMedia => MediaItems.Count > 0;
    public int MediaCount => MediaItems.Count;
    public bool HasSingleMedia => MediaItems.Count == 1;
    public bool HasMultipleMedia => MediaItems.Count > 1;
    public bool HasTwoMedia => MediaItems.Count == 2;
    public bool HasThreeOrMoreMedia => MediaItems.Count >= 3;

    public MediaItem? PrimaryMedia => MediaItems.Count > 0 ? MediaItems[0] : null;
    public MediaItem? SecondaryMedia => MediaItems.Count > 1 ? MediaItems[1] : null;
    public MediaItem? ThirdMedia => MediaItems.Count > 2 ? MediaItems[2] : null;
    public MediaItem? FourthMedia => MediaItems.Count > 3 ? MediaItems[3] : null;

    public string? PrimaryImage => PrimaryMedia?.FilePath;
    public string? SecondaryImage => SecondaryMedia?.FilePath;
    public string? ThirdImage => ThirdMedia?.FilePath;
    public string? FourthImage => FourthMedia?.FilePath;

    public string? PrimaryThumbnail => PrimaryMedia?.DisplayThumbnail ?? PrimaryMedia?.FilePath;
    public string? SecondaryThumbnail => SecondaryMedia?.DisplayThumbnail ?? SecondaryMedia?.FilePath;
    public string? ThirdThumbnail => ThirdMedia?.DisplayThumbnail ?? ThirdMedia?.FilePath;
    public string? FourthThumbnail => FourthMedia?.DisplayThumbnail ?? FourthMedia?.FilePath;

    public bool HasVideo => MediaItems.Any(m => m.IsVideo);
    public bool PrimaryIsVideo => PrimaryMedia?.IsVideo ?? false;
    public bool SecondaryIsVideo => SecondaryMedia?.IsVideo ?? false;

    public string AvatarInitial
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AuthorName)) return "?";
            var parts = AuthorName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            }
            return AuthorName.Trim().Substring(0, 1).ToUpper();
        }
    }

    public string RelativeTime
    {
        get
        {
            var span = DateTime.Now - PostDate;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d";
            if (PostDate.Year == DateTime.Now.Year) return PostDate.ToString("MMM d");
            return PostDate.ToString("MMM d, yyyy");
        }
    }

    public string FormattedDate => PostDate.ToString("MMM dd, yyyy, hh:mm tt");

    public List<string> TagList
    {
        get
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(Tags))
            {
                var matches = Regex.Matches(Tags, @"#?([a-zA-Z0-9_\-]+)");
                foreach (Match m in matches)
                {
                    var tag = m.Groups[1].Value.ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(tag) && !list.Contains(tag))
                    {
                        list.Add(tag);
                    }
                }
            }

            if (list.Count == 0 && !string.IsNullOrWhiteSpace(Content))
            {
                var matches = Regex.Matches(Content, @"#([a-zA-Z0-9_\-]+)");
                foreach (Match m in matches)
                {
                    var tag = m.Groups[1].Value.ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(tag) && !list.Contains(tag))
                    {
                        list.Add(tag);
                    }
                }
            }

            return list;
        }
    }
}

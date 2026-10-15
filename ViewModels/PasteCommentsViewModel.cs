using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;
using LocalSocialArchive.Services;

namespace LocalSocialArchive.ViewModels;

public partial class PasteCommentsViewModel : ObservableObject
{
    private readonly IEseDatabaseService _dbService;

    public Post TargetPost { get; }

    [ObservableProperty]
    private string _rawText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParsedComments))]
    [NotifyPropertyChangedFor(nameof(ParsedCountText))]
    private ObservableCollection<PostComment> _parsedComments = new();

    [ObservableProperty]
    private bool _appendToExisting = true;

    [ObservableProperty]
    private string _detectionSummary = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasParsedComments => ParsedComments.Count > 0;
    public string ParsedCountText => ParsedComments.Count == 1 ? "1 comment detected" : $"{ParsedComments.Count} comments detected";
    public int ExistingCommentCount => TargetPost.CommentCount;
    public string PostAuthor => TargetPost.AuthorName;
    public string PostHandle => TargetPost.AuthorHandle;
    public string PostPreviewSnippet => TargetPost.Content.Length > 120 
        ? TargetPost.Content.Substring(0, 120) + "..." 
        : TargetPost.Content;

    public event Action<bool>? RequestClose;

    public PasteCommentsViewModel(Post post, IEseDatabaseService dbService)
    {
        TargetPost = post;
        _dbService = dbService;

        // Auto-check clipboard upon opening to provide frictionless 1-click paste
        CheckClipboardHint();
    }

    partial void OnRawTextChanged(string value)
    {
        ParseCommentsFromText(value);
    }

    private void CheckClipboardHint()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text) && (text.Contains("@") || text.Contains("ago") || text.Contains("reply")))
                {
                    StatusMessage = "📋 Clipboard contains text that looks like comments. Click 'Paste from Clipboard' to import!";
                }
            }
        }
        catch { }
    }

    [RelayCommand]
    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    RawText = text;
                    StatusMessage = $"Imported text from clipboard ({text.Length} characters).";
                }
                else
                {
                    StatusMessage = "Clipboard text is empty.";
                }
            }
            else
            {
                StatusMessage = "No text found in clipboard.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Clipboard Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearText()
    {
        RawText = string.Empty;
        ParsedComments.Clear();
        DetectionSummary = string.Empty;
        StatusMessage = "Cleared.";
    }

    [RelayCommand]
    private void RemoveComment(PostComment? comment)
    {
        if (comment != null && ParsedComments.Contains(comment))
        {
            ParsedComments.Remove(comment);
            UpdateDetectionSummary();
        }
    }

    [RelayCommand]
    private void AddManualComment()
    {
        var newComment = new PostComment
        {
            PostId = TargetPost.Id,
            AuthorHandle = "@commenter",
            AuthorName = "Commenter",
            Content = "Type your comment here...",
            TimeAgo = "just now"
        };
        ParsedComments.Add(newComment);
        UpdateDetectionSummary();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (ParsedComments.Count == 0)
        {
            StatusMessage = "Please paste or enter at least one comment before saving.";
            return;
        }

        try
        {
            if (AppendToExisting)
            {
                foreach (var c in ParsedComments)
                {
                    c.PostId = TargetPost.Id;
                    TargetPost.Comments.Add(c);
                }
            }
            else
            {
                TargetPost.Comments.Clear();
                foreach (var c in ParsedComments)
                {
                    c.PostId = TargetPost.Id;
                    TargetPost.Comments.Add(c);
                }
            }

            TargetPost.RefreshCommentCounts();
            await _dbService.SaveCommentsForPostAsync(TargetPost.Id, TargetPost.Comments);

            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save Error: {ex.Message}";
            MessageBox.Show($"Failed to save comments: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }

    private void ParseCommentsFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ParsedComments.Clear();
            DetectionSummary = string.Empty;
            return;
        }

        var list = CommentParserHelper.Parse(text, TargetPost.Id);
        ParsedComments = new ObservableCollection<PostComment>(list);
        UpdateDetectionSummary();
    }

    private void UpdateDetectionSummary()
    {
        if (ParsedComments.Count == 0)
        {
            DetectionSummary = "No comments detected yet. Paste YouTube/Twitter/social comments in the text box.";
        }
        else
        {
            var authors = ParsedComments
                .Select(c => c.DisplayHandle)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct()
                .Take(4)
                .ToList();

            string authorSnippet = string.Join(", ", authors);
            if (ParsedComments.Count > 4) authorSnippet += "...";

            DetectionSummary = $"✓ Detected {ParsedComments.Count} comments ({authorSnippet})";
        }
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using LocalSocialArchive.Models;

namespace LocalSocialArchive.Helpers;

public static class CommentParserHelper
{
    private static readonly Regex HandleRegex = new(@"^@([a-zA-Z0-9_\-\.]{2,})$", RegexOptions.Compiled);
    private static readonly Regex InlineHandleTimeRegex = new(@"^@([a-zA-Z0-9_\-\.]+)\s*(?:[•·\-]\s*|\s+)(\d+.*ago.*|just\s+now|yesterday)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TimeAgoRegex = new(@"^(\d+\s*(second|sec|minute|min|hour|hr|day|week|wk|month|mo|year|yr)s?\s*ago(\s*\(edited\))?|just\s+now|yesterday)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LikesRegex = new(@"^(\d+([.,]\d+)?[KkMm]?)$", RegexOptions.Compiled);
    private static readonly Regex RepliesRegex = new(@"^(\d+\s+repl(?:y|ies))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TwitterHeaderRegex = new(@"^(?<name>.+?)\s+@(?<handle>[a-zA-Z0-9_]+)\s*[·•]\s*(?<time>.+)$", RegexOptions.Compiled);
    private static readonly Regex ColonCommentRegex = new(@"^@?(?<author>[a-zA-Z0-9_\-\. ]{2,30}):\s*(?<content>.+)$", RegexOptions.Compiled);

    public static List<PostComment> Parse(string rawText, string postId = "")
    {
        var results = new List<PostComment>();
        if (string.IsNullOrWhiteSpace(rawText)) return results;

        rawText = rawText.Trim();

        // 1. Try JSON Array parsing
        if (rawText.StartsWith("[") && rawText.EndsWith("]"))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawText);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        var c = new PostComment { PostId = postId };
                        if (elem.TryGetProperty("author", out var auth) || elem.TryGetProperty("authorHandle", out auth) || elem.TryGetProperty("user", out auth))
                            c.AuthorHandle = auth.GetString() ?? string.Empty;
                        if (elem.TryGetProperty("authorName", out var name) || elem.TryGetProperty("name", out name))
                            c.AuthorName = name.GetString() ?? string.Empty;
                        if (elem.TryGetProperty("content", out var cnt) || elem.TryGetProperty("text", out cnt) || elem.TryGetProperty("comment", out cnt))
                            c.Content = cnt.GetString() ?? string.Empty;
                        if (elem.TryGetProperty("time", out var tm) || elem.TryGetProperty("timeAgo", out tm))
                            c.TimeAgo = tm.GetString() ?? string.Empty;
                        if (elem.TryGetProperty("likes", out var lk) || elem.TryGetProperty("likeCount", out lk))
                            c.LikeCount = lk.GetString() ?? string.Empty;
                        if (elem.TryGetProperty("replies", out var rep) || elem.TryGetProperty("replyCount", out rep))
                            c.ReplyCount = rep.GetString() ?? string.Empty;

                        if (!string.IsNullOrWhiteSpace(c.Content))
                        {
                            results.Add(c);
                        }
                    }

                    if (results.Count > 0) return results;
                }
            }
            catch { }
        }

        // 2. Parse YouTube & Social Multi-line format
        var lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                           .Select(l => l.Trim())
                           .ToList();

        // Check if contains @handles
        int atCount = lines.Count(l => HandleRegex.IsMatch(l) || InlineHandleTimeRegex.IsMatch(l));
        if (atCount > 0)
        {
            results = ParseYouTubeMultiLine(lines, postId);
            if (results.Count > 0) return results;
        }

        // 3. Check for Twitter header format
        results = ParseTwitterFormat(lines, postId);
        if (results.Count > 0) return results;

        // 4. Check for Author: Comment lines
        results = ParseColonFormat(lines, postId);
        if (results.Count > 0) return results;

        // 5. Fallback: Parse paragraphs separated by blank lines
        results = ParseParagraphs(rawText, postId);
        return results;
    }

    private static List<PostComment> ParseYouTubeMultiLine(List<string> lines, string postId)
    {
        var comments = new List<PostComment>();
        PostComment? current = null;
        var contentLines = new List<string>();

        void FlushCurrent()
        {
            if (current != null && contentLines.Count > 0)
            {
                current.Content = string.Join("\n", contentLines).Trim();
                if (!string.IsNullOrWhiteSpace(current.Content))
                {
                    comments.Add(current);
                }
            }
            current = null;
            contentLines.Clear();
        }

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Check if this line starts a new YouTube comment
            var inlineMatch = InlineHandleTimeRegex.Match(line);
            var handleMatch = HandleRegex.Match(line);

            if (inlineMatch.Success || handleMatch.Success)
            {
                // Save previous comment
                FlushCurrent();

                current = new PostComment
                {
                    PostId = postId,
                    CreatedAt = DateTime.Now
                };

                if (inlineMatch.Success)
                {
                    current.AuthorHandle = "@" + inlineMatch.Groups[1].Value;
                    current.AuthorName = inlineMatch.Groups[1].Value;
                    current.TimeAgo = inlineMatch.Groups[2].Value.Trim();
                }
                else
                {
                    current.AuthorHandle = line;
                    current.AuthorName = line.TrimStart('@');

                    // Check if next line is timestamp (e.g. "2 years ago")
                    if (i + 1 < lines.Count && TimeAgoRegex.IsMatch(lines[i + 1]))
                    {
                        current.TimeAgo = lines[i + 1];
                        i++; // skip timestamp line
                    }
                }

                continue;
            }

            if (current != null)
            {
                // Skip UI artifacts like "Reply", "Translate to English", etc.
                if (string.Equals(line, "Reply", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(line, "Dislike", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(line, "Share", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Translated from", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Pinned by", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Check for replies count line, e.g. "15 replies"
                var repliesMatch = RepliesRegex.Match(line);
                if (repliesMatch.Success)
                {
                    current.ReplyCount = repliesMatch.Value;
                    continue;
                }

                // Check for Like count (typically digits or 1.2K before Reply or near end)
                if (LikesRegex.IsMatch(line))
                {
                    // If next line is "Reply" or "replies", this is definitely the like count
                    if (i + 1 < lines.Count && (
                        string.Equals(lines[i + 1], "Reply", StringComparison.OrdinalIgnoreCase) ||
                        RepliesRegex.IsMatch(lines[i + 1]) ||
                        HandleRegex.IsMatch(lines[i + 1])))
                    {
                        current.LikeCount = line;
                        continue;
                    }
                }

                // Regular content line
                contentLines.Add(line);
            }
        }

        FlushCurrent();
        return comments;
    }

    private static List<PostComment> ParseTwitterFormat(List<string> lines, string postId)
    {
        var comments = new List<PostComment>();
        PostComment? current = null;
        var contentLines = new List<string>();

        void FlushCurrent()
        {
            if (current != null && contentLines.Count > 0)
            {
                current.Content = string.Join("\n", contentLines).Trim();
                if (!string.IsNullOrWhiteSpace(current.Content))
                {
                    comments.Add(current);
                }
            }
            current = null;
            contentLines.Clear();
        }

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var match = TwitterHeaderRegex.Match(line);
            if (match.Success)
            {
                FlushCurrent();
                current = new PostComment
                {
                    PostId = postId,
                    AuthorName = match.Groups["name"].Value.Trim(),
                    AuthorHandle = "@" + match.Groups["handle"].Value.Trim(),
                    TimeAgo = match.Groups["time"].Value.Trim()
                };
                continue;
            }

            if (current != null)
            {
                contentLines.Add(line);
            }
        }

        FlushCurrent();
        return comments;
    }

    private static List<PostComment> ParseColonFormat(List<string> lines, string postId)
    {
        var comments = new List<PostComment>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var match = ColonCommentRegex.Match(line);
            if (match.Success)
            {
                var author = match.Groups["author"].Value.Trim();
                var content = match.Groups["content"].Value.Trim();
                comments.Add(new PostComment
                {
                    PostId = postId,
                    AuthorHandle = author.StartsWith("@") ? author : "@" + author.Replace(" ", "").ToLowerInvariant(),
                    AuthorName = author.TrimStart('@'),
                    Content = content,
                    TimeAgo = "just now"
                });
            }
        }
        return comments;
    }

    private static List<PostComment> ParseParagraphs(string rawText, string postId)
    {
        var comments = new List<PostComment>();
        var paragraphs = rawText.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        int counter = 1;
        foreach (var p in paragraphs)
        {
            var trimmed = p.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            comments.Add(new PostComment
            {
                PostId = postId,
                AuthorHandle = $"@commenter{counter}",
                AuthorName = $"Commenter {counter}",
                Content = trimmed,
                TimeAgo = "recently"
            });
            counter++;
        }

        return comments;
    }
}

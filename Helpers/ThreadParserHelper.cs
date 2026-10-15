using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using LocalSocialArchive.Models;

namespace LocalSocialArchive.Helpers;

public enum ThreadSplitMode
{
    Auto,
    Numbered,
    Dividers,
    DoubleLineBreak
}

public static partial class ThreadParserHelper
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    [GeneratedRegex(@"Thread\s+by\s+@?([A-Za-z0-9_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ThreadByHandleRegex();

    [GeneratedRegex(@"Thread\s+by\s+([^(\n\r]+?)\s+\(@([A-Za-z0-9_]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ThreadByNameAndHandleRegex();

    [GeneratedRegex(@"(?<!\w)@([A-Za-z0-9_]{1,25})")]
    private static partial Regex TwitterHandleRegex();

    [GeneratedRegex(@"(?m)^[ \t]*(?:---|\*\*\*|___|===)[ \t]*$")]
    private static partial Regex DividerLineRegex();

    [GeneratedRegex(@"(?m)^(?:\(?\s*(\d{1,3})\s*(?:[/.)]|of\s*\d{1,3})\s*|🧵\s*(\d{1,3})[/.)]\s*)")]
    private static partial Regex NumberedBoundaryRegex();

    [GeneratedRegex(@"#[A-Za-z0-9_]+")]
    private static partial Regex HashtagRegex();

    [GeneratedRegex(@"<img\s+[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTagRegex();

    [GeneratedRegex(@"(?:src|data-src)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ImgSrcRegex();

    public static (string authorName, string authorHandle, string tags, List<string> rawSegments) ParseThreadRaw(string input, ThreadSplitMode mode = ThreadSplitMode.Auto)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ("Offline User", "@offline", string.Empty, new List<string>());
        }

        string cleanInput = input.Trim();
        string authorName = "Offline User";
        string authorHandle = "@offline";

        // 1. Detect Author from header (e.g. "Thread by Naval (@naval)" or "Thread by @naval")
        var nameAndHandleMatch = ThreadByNameAndHandleRegex().Match(cleanInput);
        if (nameAndHandleMatch.Success)
        {
            authorName = nameAndHandleMatch.Groups[1].Value.Trim();
            authorHandle = "@" + nameAndHandleMatch.Groups[2].Value.Trim();
        }
        else
        {
            var byHandleMatch = ThreadByHandleRegex().Match(cleanInput);
            if (byHandleMatch.Success)
            {
                authorHandle = "@" + byHandleMatch.Groups[1].Value.Trim();
                authorName = byHandleMatch.Groups[1].Value.Trim();
            }
            else
            {
                var handleMatch = TwitterHandleRegex().Match(cleanInput);
                if (handleMatch.Success && cleanInput.IndexOf(handleMatch.Value, StringComparison.OrdinalIgnoreCase) < 120)
                {
                    authorHandle = "@" + handleMatch.Groups[1].Value.Trim();
                    authorName = handleMatch.Groups[1].Value.Trim();
                }
            }
        }

        // 2. Extract Hashtags
        var hashtagMatches = HashtagRegex().Matches(cleanInput);
        var tagsList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in hashtagMatches)
        {
            tagsList.Add(m.Value.ToLowerInvariant());
        }
        string tags = string.Join(" ", tagsList);

        // 3. Remove Thread Reader Boilerplate Header & Footer
        cleanInput = StripThreadReaderBoilerplate(cleanInput);

        // 4. Split into segments based on mode
        var segments = SplitIntoSegments(cleanInput, mode);

        return (authorName, authorHandle, tags, segments);
    }

    private static string StripThreadReaderBoilerplate(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
        var resultLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Thread by ", StringComparison.OrdinalIgnoreCase) && trimmed.Length < 100)
                continue;
            if (trimmed.Equals("Read on Twitter", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Read 1 tweet", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Read on X", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Missing some Tweets?", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("• • •", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Don't forget to like and retweet", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Thread Reader App", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            resultLines.Add(line);
        }

        return string.Join(Environment.NewLine, resultLines);
    }

    private static List<string> SplitIntoSegments(string text, ThreadSplitMode mode)
    {
        var segments = new List<string>();

        if (mode == ThreadSplitMode.Dividers || (mode == ThreadSplitMode.Auto && DividerLineRegex().IsMatch(text)))
        {
            var parts = DividerLineRegex().Split(text);
            foreach (var p in parts)
            {
                var clean = p.Trim();
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    segments.Add(clean);
                }
            }
            if (segments.Count > 1) return segments;
            segments.Clear();
        }

        if (mode == ThreadSplitMode.Numbered || mode == ThreadSplitMode.Auto)
        {
            var matches = NumberedBoundaryRegex().Matches(text);
            if (matches.Count >= 2)
            {
                int lastIndex = 0;
                for (int i = 0; i < matches.Count; i++)
                {
                    int matchPos = matches[i].Index;
                    if (matchPos > lastIndex)
                    {
                        string piece = text.Substring(lastIndex, matchPos - lastIndex).Trim();
                        if (!string.IsNullOrWhiteSpace(piece))
                        {
                            segments.Add(piece);
                        }
                    }

                    int nextPos = (i + 1 < matches.Count) ? matches[i + 1].Index : text.Length;
                    string tweetContent = text.Substring(matchPos, nextPos - matchPos).Trim();
                    if (!string.IsNullOrWhiteSpace(tweetContent))
                    {
                        segments.Add(tweetContent);
                    }
                    lastIndex = nextPos;
                }

                if (segments.Count > 1) return segments;
                segments.Clear();
            }
        }

        // Fallback: Double line breaks / paragraphs
        var paragraphParts = Regex.Split(text, @"(?:\r?\n){2,}");
        foreach (var p in paragraphParts)
        {
            var clean = p.Trim();
            if (!string.IsNullOrWhiteSpace(clean))
            {
                segments.Add(clean);
            }
        }

        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            segments.Add(text.Trim());
        }

        return segments;
    }

    public static List<string> ExtractImageUrlsFromHtmlOrText(string raw)
    {
        var urls = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return urls;

        // Check <img> tags
        var imgMatches = ImgTagRegex().Matches(raw);
        foreach (Match img in imgMatches)
        {
            var srcMatch = ImgSrcRegex().Match(img.Value);
            if (srcMatch.Success)
            {
                string url = srcMatch.Groups[1].Value.Trim();
                if (!urls.Contains(url) && (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:image", StringComparison.OrdinalIgnoreCase)))
                {
                    urls.Add(url);
                }
            }
        }

        // Check direct twitter media URLs in plain text (pbs.twimg.com/media/...)
        var directUrlMatches = Regex.Matches(raw, @"https?://pbs\.twimg\.com/media/[A-Za-z0-9_-]+\.(?:jpg|jpeg|png|webp)", RegexOptions.IgnoreCase);
        foreach (Match m in directUrlMatches)
        {
            if (!urls.Contains(m.Value))
            {
                urls.Add(m.Value);
            }
        }

        return urls;
    }

    public static async Task<List<string>> DownloadMediaUrlsAsync(IEnumerable<string> urls, string targetDirectory, CancellationToken cancellationToken = default)
    {
        var savedPaths = new List<string>();
        var urlList = urls.Distinct().ToList();
        if (urlList.Count == 0) return savedPaths;

        Directory.CreateDirectory(targetDirectory);
        using var throttle = new SemaphoreSlim(3, 3);
        int fileIndex = 1;

        var tasks = urlList.Select(async url =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    int commaIdx = url.IndexOf(',');
                    if (commaIdx > 0)
                    {
                        var base64 = url.Substring(commaIdx + 1);
                        var bytes = Convert.FromBase64String(base64);
                        string ext = url.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || url.Contains("jpg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
                        string fileName = $"thread_img_{Interlocked.Increment(ref fileIndex)}{ext}";
                        string filePath = Path.Combine(targetDirectory, fileName);
                        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
                        lock (savedPaths) { savedPaths.Add(filePath); }
                    }
                    return;
                }

                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    string ext = Path.GetExtension(new Uri(url).AbsolutePath);
                    if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5) ext = ".jpg";
                    string fileName = $"thread_img_{Interlocked.Increment(ref fileIndex)}{ext}";
                    string filePath = Path.Combine(targetDirectory, fileName);

                    using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                        await stream.CopyToAsync(fileStream, cancellationToken);
                        lock (savedPaths) { savedPaths.Add(filePath); }
                    }
                }
            }
            catch
            {
                // Graceful fallback for unavailable images
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
        return savedPaths.OrderBy(p => p).ToList();
    }
}

using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace LocalSocialArchive.Helpers;

public static class HtmlClipboardHelper
{
    private static readonly HttpClient HttpClientInstance;

    static HtmlClipboardHelper()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        HttpClientInstance = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        HttpClientInstance.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        HttpClientInstance.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
    }

    public static bool HasHtml(IDataObject? dataObject = null)
    {
        var data = dataObject ?? Clipboard.GetDataObject();
        return data != null && data.GetDataPresent(DataFormats.Html);
    }

    public static string? GetHtml(IDataObject? dataObject = null)
    {
        var data = dataObject ?? Clipboard.GetDataObject();
        if (data == null || !data.GetDataPresent(DataFormats.Html)) return null;

        try
        {
            return data.GetData(DataFormats.Html) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts all image source URLs (HTTP/HTTPS, data URI, or relative) from clipboard HTML.
    /// </summary>
    public static List<string> ExtractImageSources(string html)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(html)) return results;

        // Try finding SourceURL header for resolving relative URLs
        string? baseUrl = null;
        var sourceUrlMatch = Regex.Match(html, @"SourceURL:(https?://[^\r\n]+)", RegexOptions.IgnoreCase);
        if (sourceUrlMatch.Success)
        {
            baseUrl = sourceUrlMatch.Groups[1].Value.Trim();
        }

        // Match <img ... src="..." ...> and data-src / data-original (common in forum lazy-loaders)
        var imgTagMatches = Regex.Matches(html, @"<img\s+[^>]*>", RegexOptions.IgnoreCase);
        foreach (Match tagMatch in imgTagMatches)
        {
            string tag = tagMatch.Value;

            // Prefer data-src or data-original if present (lazy loaded forum images)
            string? src = null;
            var dataSrcMatch = Regex.Match(tag, @"data-src\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (dataSrcMatch.Success && !string.IsNullOrWhiteSpace(dataSrcMatch.Groups[1].Value))
            {
                src = dataSrcMatch.Groups[1].Value.Trim();
            }

            if (string.IsNullOrWhiteSpace(src))
            {
                var srcMatch = Regex.Match(tag, @"src\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (srcMatch.Success)
                {
                    src = srcMatch.Groups[1].Value.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(src)) continue;

            // Skip tiny tracking pixels or spacers
            if (src.Contains("pixel.gif") || src.Contains("spacer.gif") || src.Contains("1x1"))
            {
                continue;
            }

            // Resolve relative URLs
            if (!src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !src.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(new Uri(baseUrl), src, out var resolvedUri))
                {
                    src = resolvedUri.ToString();
                }
            }

            if (!results.Contains(src, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(src);
            }
        }

        return results;
    }

    /// <summary>
    /// Downloads image URLs (HTTP or base64) into the target directory and returns their local file paths.
    /// </summary>
    public static async Task<List<string>> DownloadHtmlImagesAsync(string html, string targetDirectory, int maxImages = 15)
    {
        var localPaths = new List<string>();
        var imageSources = ExtractImageSources(html);
        if (imageSources.Count == 0) return localPaths;

        Directory.CreateDirectory(targetDirectory);
        int index = 1;

        foreach (var src in imageSources.Take(maxImages))
        {
            try
            {
                // 1. Base64 Data URI
                if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    int commaIdx = src.IndexOf(',');
                    if (commaIdx > 0)
                    {
                        string meta = src.Substring(0, commaIdx);
                        string base64Data = src.Substring(commaIdx + 1);
                        string ext = ".png";
                        if (meta.Contains("jpeg") || meta.Contains("jpg")) ext = ".jpg";
                        else if (meta.Contains("webp")) ext = ".webp";
                        else if (meta.Contains("gif")) ext = ".gif";

                        byte[] bytes = Convert.FromBase64String(base64Data);
                        string filePath = Path.Combine(targetDirectory, $"pasted_img_{DateTime.Now:yyyyMMdd_HHmmss}_{index++}{ext}");
                        await File.WriteAllBytesAsync(filePath, bytes);
                        localPaths.Add(filePath);
                    }
                    continue;
                }

                // 2. HTTP / HTTPS URL
                if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var response = await HttpClientInstance.GetAsync(src, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode) continue;

                    var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                    string ext = ".jpg";
                    if (contentType.Contains("png")) ext = ".png";
                    else if (contentType.Contains("webp")) ext = ".webp";
                    else if (contentType.Contains("gif")) ext = ".gif";
                    else if (contentType.Contains("jpeg") || contentType.Contains("jpg")) ext = ".jpg";
                    else
                    {
                        var urlExt = Path.GetExtension(new Uri(src).AbsolutePath).ToLowerInvariant();
                        if (ClipboardHelper.IsImageExtension(urlExt)) ext = urlExt;
                    }

                    byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
                    if (imageBytes.Length < 100) continue; // Skip 0-byte or corrupted 1x1 pixels

                    string filePath = Path.Combine(targetDirectory, $"forum_img_{DateTime.Now:yyyyMMdd_HHmmss}_{index++}{ext}");
                    await File.WriteAllBytesAsync(filePath, imageBytes);
                    localPaths.Add(filePath);
                }
            }
            catch
            {
                // Silently skip inaccessible or restricted images without breaking the paste flow
            }
        }

        return localPaths;
    }

    /// <summary>
    /// Converts forum / HTML clipboard content to clean readable markdown/text, stripping noise and preserving structure.
    /// </summary>
    public static string ConvertHtmlToFormattedText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        // Isolate fragment if CF_HTML headers exist
        int startFrag = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        int endFrag = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        string content = html;
        if (startFrag >= 0 && endFrag > startFrag)
        {
            content = html.Substring(startFrag + "<!--StartFragment-->".Length, endFrag - startFrag - "<!--StartFragment-->".Length);
        }

        // Clean common tags
        content = Regex.Replace(content, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"<!--[\s\S]*?-->", "");

        // Convert structural block tags to newlines
        content = Regex.Replace(content, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"</p>", "\n\n", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"</div>", "\n", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"</li>", "\n", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"<h[1-6][^>]*>(.*?)</h[1-6]>", "\n\n# $1\n\n", RegexOptions.IgnoreCase);
        content = Regex.Replace(content, @"<blockquote[^>]*>(.*?)</blockquote>", "\n\n> $1\n\n", RegexOptions.IgnoreCase);

        // Strip remaining HTML tags
        content = Regex.Replace(content, @"<[^>]+>", "");

        // Decode standard HTML entities
        content = System.Net.WebUtility.HtmlDecode(content);

        // Clean excessive blank lines
        content = Regex.Replace(content, @"\n{3,}", "\n\n");
        return content.Trim();
    }
}

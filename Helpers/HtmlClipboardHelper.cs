using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace LocalSocialArchive.Helpers;

/// <summary>
/// Fully offline helper to process clipboard HTML content and inline base64 media without any network requests.
/// </summary>
public static class HtmlClipboardHelper
{
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
    /// Extracts local inline image sources (e.g., base64 data URIs) from clipboard HTML.
    /// Strictly offline: ignores remote HTTP/HTTPS endpoints to prevent network leaks.
    /// </summary>
    public static List<string> ExtractImageSources(string html)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(html)) return results;

        var imgTagMatches = Regex.Matches(html, @"<img\s+[^>]*>", RegexOptions.IgnoreCase);
        foreach (Match tagMatch in imgTagMatches)
        {
            string tag = tagMatch.Value;
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

            // Only extract data:image base64 URIs (100% offline clipboard data)
            if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                if (!results.Contains(src, StringComparer.OrdinalIgnoreCase))
                {
                    results.Add(src);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Decodes embedded base64 data URIs into the target directory and returns their local file paths.
    /// 100% offline: makes zero network calls.
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
                }
            }
            catch
            {
                // Silently skip corrupted base64 payloads
            }
        }

        return localPaths;
    }

    /// <summary>
    /// Converts forum / HTML clipboard content to clean readable markdown/text, stripping noise and preserving structure.
    /// Replaces remote image tags with readable placeholders to maintain 100% offline privacy.
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

        // Clean common script/style tags
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

        // Replace remote images with offline placeholders
        content = Regex.Replace(content, @"<img\s+[^>]*src=[""'](https?://[^""']+)[""'][^>]*>", "\n🖼️ [Remote Image: $1]\n", RegexOptions.IgnoreCase);

        // Strip remaining HTML tags
        content = Regex.Replace(content, @"<[^>]+>", "");

        // Decode standard HTML entities
        content = System.Net.WebUtility.HtmlDecode(content);

        // Clean excessive blank lines
        content = Regex.Replace(content, @"\n{3,}", "\n\n");
        return content.Trim();
    }
}

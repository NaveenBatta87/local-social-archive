using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using LocalSocialArchive.Models;

namespace LocalSocialArchive.Helpers;

public static partial class CanvasDocumentHelper
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    [GeneratedRegex(@"!\[(.*?)\]\((.*?)\)")]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"<img\s+[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTagRegex();

    [GeneratedRegex(@"(?:src|data-src)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ImgSrcRegex();

    public static List<CanvasBlock> ParseFromContent(string content, IEnumerable<MediaItem>? mediaItems = null)
    {
        var blocks = new List<CanvasBlock>();
        if (string.IsNullOrWhiteSpace(content))
        {
            blocks.Add(new CanvasBlock { Type = CanvasBlockType.Paragraph, Text = string.Empty });
            return blocks;
        }

        // Split by lines / markdown image tags
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var currentPara = new StringBuilder();

        foreach (var line in lines)
        {
            var imgMatch = MarkdownImageRegex().Match(line);
            if (imgMatch.Success)
            {
                // Flush preceding paragraph
                if (currentPara.Length > 0)
                {
                    var text = currentPara.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        blocks.Add(new CanvasBlock { Type = CanvasBlockType.Paragraph, Text = text });
                    }
                    currentPara.Clear();
                }

                string imgPath = imgMatch.Groups[2].Value.Trim();
                long size = 0;
                if (File.Exists(imgPath))
                {
                    try { size = new FileInfo(imgPath).Length; } catch { }
                }

                blocks.Add(new CanvasBlock
                {
                    Type = CanvasBlockType.Image,
                    ImagePath = imgPath,
                    Caption = imgMatch.Groups[1].Value.Trim(),
                    ImageSizeBytes = size
                });
            }
            else
            {
                currentPara.AppendLine(line);
            }
        }

        if (currentPara.Length > 0)
        {
            var text = currentPara.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                blocks.Add(new CanvasBlock { Type = CanvasBlockType.Paragraph, Text = text });
            }
        }

        // If there are media items that weren't embedded inline in the content, keep them accounted for
        if (mediaItems != null)
        {
            var embeddedPaths = new HashSet<string>(blocks.Where(b => b.IsImage && !string.IsNullOrEmpty(b.ImagePath)).Select(b => b.ImagePath!), StringComparer.OrdinalIgnoreCase);
            foreach (var m in mediaItems)
            {
                if (!string.IsNullOrEmpty(m.FilePath) && File.Exists(m.FilePath) && !embeddedPaths.Contains(m.FilePath))
                {
                    long size = 0;
                    try { size = new FileInfo(m.FilePath).Length; } catch { }
                    blocks.Add(new CanvasBlock
                    {
                        Type = CanvasBlockType.Image,
                        ImagePath = m.FilePath,
                        ImageSizeBytes = size
                    });
                }
            }
        }

        if (blocks.Count == 0)
        {
            blocks.Add(new CanvasBlock { Type = CanvasBlockType.Paragraph, Text = string.Empty });
        }

        return blocks;
    }

    public static string SerializeToMarkdown(IEnumerable<CanvasBlock> blocks)
    {
        var sb = new StringBuilder();
        foreach (var b in blocks)
        {
            if (b.IsParagraph && !string.IsNullOrWhiteSpace(b.Text))
            {
                sb.AppendLine(b.Text.Trim());
                sb.AppendLine();
            }
            else if (b.IsImage && !string.IsNullOrEmpty(b.ImagePath))
            {
                string caption = !string.IsNullOrWhiteSpace(b.Caption) ? b.Caption : "Image";
                sb.AppendLine($"![{caption}]({b.ImagePath})");
                sb.AppendLine();
            }
        }
        return sb.ToString().Trim();
    }

    public static async Task<List<CanvasBlock>> ParseHtmlOrTextWithInlineImagesAsync(string raw, string targetMediaDir)
    {
        var blocks = new List<CanvasBlock>();
        if (string.IsNullOrWhiteSpace(raw)) return blocks;

        Directory.CreateDirectory(targetMediaDir);

        // If raw is standard plain text without HTML tags
        if (!raw.Contains("<") || !raw.Contains(">"))
        {
            return ParseFromContent(raw);
        }

        // Parse HTML sequentially preserving inline positions
        // Extract fragments
        string html = raw;
        int startFrag = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        int endFrag = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (startFrag >= 0 && endFrag > startFrag)
        {
            html = html.Substring(startFrag + "<!--StartFragment-->".Length, endFrag - startFrag - "<!--StartFragment-->".Length);
        }

        // Clean script/style noise
        html = Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);

        // Split by <img> tags or paragraph dividers
        // Tokenize HTML by replacing <img ...> with unique marker tags so order is 100% preserved
        var imgMatches = ImgTagRegex().Matches(html);
        var imgMap = new Dictionary<string, string>(); // token -> src
        int markerIdx = 1;

        string tokenizedHtml = ImgTagRegex().Replace(html, m =>
        {
            var srcMatch = ImgSrcRegex().Match(m.Value);
            if (srcMatch.Success)
            {
                string src = srcMatch.Groups[1].Value.Trim();
                string token = $"__INLINE_IMG_MARKER_{markerIdx++}__";
                imgMap[token] = src;
                return $"\n\n{token}\n\n";
            }
            return string.Empty;
        });

        // Convert structural HTML elements to newlines
        tokenizedHtml = Regex.Replace(tokenizedHtml, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        tokenizedHtml = Regex.Replace(tokenizedHtml, @"</p>", "\n\n", RegexOptions.IgnoreCase);
        tokenizedHtml = Regex.Replace(tokenizedHtml, @"</div>", "\n", RegexOptions.IgnoreCase);
        tokenizedHtml = Regex.Replace(tokenizedHtml, @"</li>", "\n", RegexOptions.IgnoreCase);
        tokenizedHtml = Regex.Replace(tokenizedHtml, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        tokenizedHtml = Regex.Replace(tokenizedHtml, @"<h[1-6][^>]*>(.*?)</h[1-6]>", "\n\n# $1\n\n", RegexOptions.IgnoreCase);

        // Strip HTML tags (except our markers)
        string plainTextWithMarkers = Regex.Replace(tokenizedHtml, @"<[^>]+>", "");
        plainTextWithMarkers = System.Net.WebUtility.HtmlDecode(plainTextWithMarkers);

        // Now split text by lines / markers
        var parts = Regex.Split(plainTextWithMarkers, @"(__INLINE_IMG_MARKER_\d+__)");
        int fileCounter = 1;

        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;

            if (imgMap.TryGetValue(p.Trim(), out var src))
            {
                // Download or decode image
                string? savedPath = await SaveImageLocallyAsync(src, targetMediaDir, fileCounter++);
                if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
                {
                    long size = new FileInfo(savedPath).Length;
                    blocks.Add(new CanvasBlock
                    {
                        Type = CanvasBlockType.Image,
                        ImagePath = savedPath,
                        ImageSizeBytes = size
                    });
                }
            }
            else
            {
                var clean = p.Trim();
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    blocks.Add(new CanvasBlock
                    {
                        Type = CanvasBlockType.Paragraph,
                        Text = clean
                    });
                }
            }
        }

        if (blocks.Count == 0)
        {
            blocks.Add(new CanvasBlock { Type = CanvasBlockType.Paragraph, Text = string.Empty });
        }

        return blocks;
    }

    private static async Task<string?> SaveImageLocallyAsync(string src, string targetDir, int index)
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
                    string ext = meta.Contains("jpeg") || meta.Contains("jpg") ? ".jpg" : (meta.Contains("webp") ? ".webp" : ".png");
                    byte[] bytes = Convert.FromBase64String(base64Data);
                    string filePath = Path.Combine(targetDir, $"doc_img_{DateTime.Now:yyyyMMdd_HHmmss}_{index}{ext}");
                    await File.WriteAllBytesAsync(filePath, bytes);
                    return filePath;
                }
            }
            else if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                string ext = Path.GetExtension(new Uri(src).AbsolutePath);
                if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5) ext = ".jpg";
                string filePath = Path.Combine(targetDir, $"doc_img_{DateTime.Now:yyyyMMdd_HHmmss}_{index}{ext}");

                using var response = await _httpClient.GetAsync(src, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await stream.CopyToAsync(fileStream);
                    return filePath;
                }
            }
            else if (File.Exists(src))
            {
                return src;
            }
        }
        catch { }

        return null;
    }

    public static void CleanTextStyling(IEnumerable<CanvasBlock> blocks)
    {
        foreach (var b in blocks)
        {
            if (b.IsParagraph && !string.IsNullOrEmpty(b.Text))
            {
                string t = b.Text;
                t = Regex.Replace(t, @"<[^>]+>", "");
                t = System.Net.WebUtility.HtmlDecode(t);
                t = Regex.Replace(t, @"\*{2,}", "");
                t = Regex.Replace(t, @"_{2,}", "");
                t = Regex.Replace(t, @"[ \t]+", " ");
                b.Text = t.Trim();
            }
        }
    }

    public static System.Windows.Documents.FlowDocument CreateFlowDocument(IEnumerable<CanvasBlock> blocks, double fontSize = 15.5)
    {
        var doc = new System.Windows.Documents.FlowDocument
        {
            PageWidth = 820,
            PagePadding = new System.Windows.Thickness(56, 44, 56, 44),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2B, 0x2B, 0x2B)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE1, 0xE1, 0xE1)),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
            FontSize = fontSize,
            TextAlignment = System.Windows.TextAlignment.Left
        };

        foreach (var b in blocks)
        {
            if (b.IsParagraph)
            {
                var p = new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(b.Text ?? string.Empty))
                {
                    Margin = new System.Windows.Thickness(0, 0, 0, 14),
                    LineHeight = fontSize * 1.5
                };
                doc.Blocks.Add(p);
            }
            else if (b.IsImage && !string.IsNullOrEmpty(b.ImagePath) && File.Exists(b.ImagePath))
            {
                var container = new System.Windows.Documents.BlockUIContainer
                {
                    Margin = new System.Windows.Thickness(0, 8, 0, 16)
                };

                var border = new System.Windows.Controls.Border
                {
                    CornerRadius = new System.Windows.CornerRadius(8),
                    ClipToBounds = true,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x18, 0x18, 0x18)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0x38, 0x38)),
                    BorderThickness = new System.Windows.Thickness(1),
                    MaxWidth = 720,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };

                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(b.ImagePath, UriKind.Absolute);
                bmp.DecodePixelWidth = 1080;
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                var img = new System.Windows.Controls.Image
                {
                    Source = bmp,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    MaxHeight = 540,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                img.Tag = b.ImagePath; // store path in tag for serialization

                border.Child = img;
                container.Child = border;
                doc.Blocks.Add(container);
            }
        }

        if (doc.Blocks.Count == 0)
        {
            doc.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(string.Empty)));
        }

        return doc;
    }

    public static List<CanvasBlock> ExtractBlocksFromFlowDocument(System.Windows.Documents.FlowDocument doc)
    {
        var blocks = new List<CanvasBlock>();

        foreach (var block in doc.Blocks)
        {
            if (block is System.Windows.Documents.Paragraph p)
            {
                var tr = new System.Windows.Documents.TextRange(p.ContentStart, p.ContentEnd);
                string text = tr.Text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    blocks.Add(new CanvasBlock
                    {
                        Type = CanvasBlockType.Paragraph,
                        Text = text
                    });
                }
            }
            else if (block is System.Windows.Documents.BlockUIContainer container)
            {
                if (container.Child is System.Windows.Controls.Border border && border.Child is System.Windows.Controls.Image img)
                {
                    string? path = img.Tag as string;
                    if (string.IsNullOrEmpty(path) && img.Source is System.Windows.Media.Imaging.BitmapImage bmp && bmp.UriSource != null)
                    {
                        path = bmp.UriSource.LocalPath;
                    }

                    if (!string.IsNullOrEmpty(path))
                    {
                        long size = 0;
                        if (File.Exists(path))
                        {
                            try { size = new FileInfo(path).Length; } catch { }
                        }

                        blocks.Add(new CanvasBlock
                        {
                            Type = CanvasBlockType.Image,
                            ImagePath = path,
                            ImageSizeBytes = size
                        });
                    }
                }
            }
        }

        return blocks;
    }

    public static void CleanFlowDocumentTextStyling(System.Windows.Documents.FlowDocument doc)
    {
        var cleanForeground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE1, 0xE1, 0xE1));
        var cleanFont = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif");

        foreach (var block in doc.Blocks)
        {
            if (block is System.Windows.Documents.Paragraph p)
            {
                var tr = new System.Windows.Documents.TextRange(p.ContentStart, p.ContentEnd);
                string text = tr.Text.Trim();

                p.Inlines.Clear();
                p.Foreground = cleanForeground;
                p.FontFamily = cleanFont;
                p.FontSize = 15.5;
                p.FontWeight = System.Windows.FontWeights.Normal;
                p.FontStyle = System.Windows.FontStyles.Normal;
                p.TextDecorations = null;
                p.Background = System.Windows.Media.Brushes.Transparent;

                if (!string.IsNullOrEmpty(text))
                {
                    p.Inlines.Add(new System.Windows.Documents.Run(text));
                }
            }
        }
    }
}

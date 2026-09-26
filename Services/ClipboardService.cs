using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;

namespace LocalSocialArchive.Services;

public class ClipboardService : IClipboardService
{
    private readonly IEseDatabaseService _dbService;
    private readonly string _mediaBaseDirectory;

    public ClipboardService(IEseDatabaseService dbService)
    {
        _dbService = dbService;
        string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _mediaBaseDirectory = Path.Combine(userDocs, "LocalSocialArchive", "Media");
        Directory.CreateDirectory(_mediaBaseDirectory);
    }

    public async Task<Post?> ProcessClipboardAsync(string targetFolder = "Coding")
    {
        string? textContent = null;
        BitmapSource? imageContent = null;
        List<string>? fileList = null;

        Application.Current.Dispatcher.Invoke(() =>
        {
            var data = Clipboard.GetDataObject();
            if (data != null)
            {
                imageContent = ClipboardHelper.GetImageFromClipboard(data);
                fileList = ClipboardHelper.GetMediaFilesFromClipboard(data);

                if (Clipboard.ContainsFileDropList())
                {
                    var dropList = Clipboard.GetFileDropList();
                    if (fileList == null) fileList = new List<string>();
                    foreach (var file in dropList)
                    {
                        if (file != null && !fileList.Contains(file)) fileList.Add(file);
                    }
                }

                if (Clipboard.ContainsText())
                {
                    textContent = Clipboard.GetText();
                }
            }
        });

        if (string.IsNullOrWhiteSpace(textContent) && imageContent == null && (fileList == null || fileList.Count == 0))
        {
            return null;
        }

        string postId = Guid.NewGuid().ToString("N");
        string folderDatePrefix = DateTime.Now.ToString("yyyyMMdd");
        string postDirectory = Path.Combine(_mediaBaseDirectory, $"{folderDatePrefix}_{postId.Substring(0, 8)}");
        Directory.CreateDirectory(postDirectory);

        var mediaItems = new List<MediaItem>();

        // 1. Process Image from Clipboard (DIB, PNG stream, BitmapSource)
        if (imageContent != null)
        {
            string imageFileName = "clipboard_image.png";
            string savedImagePath = Path.Combine(postDirectory, imageFileName);
            SaveOptimizedPng(imageContent, savedImagePath);

            mediaItems.Add(new MediaItem
            {
                Id = Guid.NewGuid().ToString("N"),
                PostId = postId,
                FilePath = savedImagePath,
                MediaType = "Image",
                CreatedAt = DateTime.Now
            });
        }

        // 2. Process Dropped / Copied Files (.jfif, .jif, .png, .mp4, etc.)
        if (fileList != null && fileList.Count > 0)
        {
            int fileIndex = 1;
            foreach (var filePath in fileList)
            {
                if (!File.Exists(filePath)) continue;

                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (MediaItem.IsVideoExtension(filePath))
                {
                    string targetFileName = $"video_{fileIndex++}{ext}";
                    string targetPath = Path.Combine(postDirectory, targetFileName);
                    if (!string.Equals(filePath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(filePath, targetPath, true);
                    }

                    string thumbPath = Path.Combine(postDirectory, $"{Path.GetFileNameWithoutExtension(targetFileName)}_thumb.png");
                    await ThumbnailHelper.GenerateVideoThumbnailAsync(targetPath, thumbPath);

                    mediaItems.Add(new MediaItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        PostId = postId,
                        FilePath = targetPath,
                        ThumbnailPath = File.Exists(thumbPath) ? thumbPath : null,
                        MediaType = "Video",
                        CreatedAt = DateTime.Now
                    });
                }
                else if (MediaItem.IsImageExtension(filePath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();

                        string targetFileName = $"media_{fileIndex++}.png";
                        string targetPath = Path.Combine(postDirectory, targetFileName);
                        SaveOptimizedPng(bitmap, targetPath);

                        mediaItems.Add(new MediaItem
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            PostId = postId,
                            FilePath = targetPath,
                            MediaType = "Image",
                            CreatedAt = DateTime.Now
                        });
                    }
                    catch
                    {
                        string destFile = Path.Combine(postDirectory, Path.GetFileName(filePath));
                        if (!string.Equals(filePath, destFile, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(filePath, destFile, true);
                        }

                        mediaItems.Add(new MediaItem
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            PostId = postId,
                            FilePath = destFile,
                            MediaType = "Image",
                            CreatedAt = DateTime.Now
                        });
                    }
                }
                else if (ext is ".txt" or ".md" or ".json")
                {
                    try
                    {
                        var fileText = File.ReadAllText(filePath);
                        if (string.IsNullOrWhiteSpace(textContent))
                        {
                            textContent = fileText;
                        }
                    }
                    catch { }
                }
                else
                {
                    string destFile = Path.Combine(postDirectory, Path.GetFileName(filePath));
                    if (!string.Equals(filePath, destFile, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(filePath, destFile, true);
                    }

                    mediaItems.Add(new MediaItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        PostId = postId,
                        FilePath = destFile,
                        MediaType = "File",
                        CreatedAt = DateTime.Now
                    });
                }
            }
        }

        // 3. Parse Metadata & Content
        var (authorName, authorHandle, parsedContent, tags) = ParseSocialMetadata(textContent, mediaItems.Count);

        var post = new Post
        {
            Id = postId,
            AuthorName = authorName,
            AuthorHandle = authorHandle,
            Content = parsedContent,
            PostDate = DateTime.Now,
            FolderName = targetFolder,
            Tags = tags,
            FolderPath = postDirectory,
            CreatedAt = DateTime.Now,
            MediaItems = new ObservableCollection<MediaItem>(mediaItems)
        };

        // 4. Write Fallback Markdown & JSON Files
        await WriteFallbackFilesAsync(post, postDirectory);

        // 5. Ingest into ESE Database
        await _dbService.InsertPostAsync(post);

        return post;
    }

    public async Task<Post?> ProcessFilesAsync(IEnumerable<string> filePaths, string? customContent = null, string targetFolder = "Coding")
    {
        string postId = Guid.NewGuid().ToString("N");
        string folderDatePrefix = DateTime.Now.ToString("yyyyMMdd");
        string postDirectory = Path.Combine(_mediaBaseDirectory, $"{folderDatePrefix}_{postId.Substring(0, 8)}");
        Directory.CreateDirectory(postDirectory);

        var mediaItems = new List<MediaItem>();
        int fileIndex = 1;
        string? textFromFile = null;

        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath)) continue;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (MediaItem.IsVideoExtension(filePath))
            {
                string targetFileName = $"video_{fileIndex++}{ext}";
                string targetPath = Path.Combine(postDirectory, targetFileName);
                if (!string.Equals(filePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(filePath, targetPath, true);
                }

                string thumbPath = Path.Combine(postDirectory, $"{Path.GetFileNameWithoutExtension(targetFileName)}_thumb.png");
                await ThumbnailHelper.GenerateVideoThumbnailAsync(targetPath, thumbPath);

                mediaItems.Add(new MediaItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PostId = postId,
                    FilePath = targetPath,
                    ThumbnailPath = File.Exists(thumbPath) ? thumbPath : null,
                    MediaType = "Video",
                    CreatedAt = DateTime.Now
                });
            }
            else if (MediaItem.IsImageExtension(filePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();

                    string targetFileName = $"media_{fileIndex++}.png";
                    string targetPath = Path.Combine(postDirectory, targetFileName);
                    SaveOptimizedPng(bitmap, targetPath);

                    mediaItems.Add(new MediaItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        PostId = postId,
                        FilePath = targetPath,
                        MediaType = "Image",
                        CreatedAt = DateTime.Now
                    });
                }
                catch
                {
                    string destFile = Path.Combine(postDirectory, Path.GetFileName(filePath));
                    if (!string.Equals(filePath, destFile, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(filePath, destFile, true);
                    }

                    mediaItems.Add(new MediaItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        PostId = postId,
                        FilePath = destFile,
                        MediaType = "Image",
                        CreatedAt = DateTime.Now
                    });
                }
            }
            else if (ext is ".txt" or ".md" or ".json" && string.IsNullOrWhiteSpace(textFromFile))
            {
                try { textFromFile = File.ReadAllText(filePath); } catch { }
            }
            else
            {
                string destFile = Path.Combine(postDirectory, Path.GetFileName(filePath));
                if (!string.Equals(filePath, destFile, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(filePath, destFile, true);
                }

                mediaItems.Add(new MediaItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    PostId = postId,
                    FilePath = destFile,
                    MediaType = "File",
                    CreatedAt = DateTime.Now
                });
            }
        }

        string rawText = customContent ?? textFromFile ?? string.Empty;
        var (authorName, authorHandle, parsedContent, tags) = ParseSocialMetadata(rawText, mediaItems.Count);

        var post = new Post
        {
            Id = postId,
            AuthorName = authorName,
            AuthorHandle = authorHandle,
            Content = parsedContent,
            PostDate = DateTime.Now,
            FolderName = targetFolder,
            Tags = tags,
            FolderPath = postDirectory,
            CreatedAt = DateTime.Now,
            MediaItems = new ObservableCollection<MediaItem>(mediaItems)
        };

        await WriteFallbackFilesAsync(post, postDirectory);
        await _dbService.InsertPostAsync(post);

        return post;
    }

    public async Task<Post?> CreateManualPostAsync(
        string authorName,
        string authorHandle,
        string content,
        string folder,
        string tags,
        IEnumerable<string>? mediaPaths = null,
        string? comment = null)
    {
        string postId = Guid.NewGuid().ToString("N");
        string folderDatePrefix = DateTime.Now.ToString("yyyyMMdd");
        string postDirectory = Path.Combine(_mediaBaseDirectory, $"{folderDatePrefix}_{postId.Substring(0, 8)}");
        Directory.CreateDirectory(postDirectory);

        var mediaItems = new List<MediaItem>();
        if (mediaPaths != null)
        {
            int index = 1;
            foreach (var path in mediaPaths)
            {
                if (File.Exists(path))
                {
                    var ext = Path.GetExtension(path);
                    bool isVid = MediaItem.IsVideoExtension(path);
                    string fileName = $"{(isVid ? "video" : "media")}_{index++}{ext}";
                    string dest = Path.Combine(postDirectory, fileName);

                    if (!string.Equals(path, dest, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(path, dest, true);
                    }

                    string? thumbPath = null;
                    if (isVid)
                    {
                        thumbPath = Path.Combine(postDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}_thumb.png");
                        await ThumbnailHelper.GenerateVideoThumbnailAsync(dest, thumbPath);
                        if (!File.Exists(thumbPath)) thumbPath = null;
                    }

                    mediaItems.Add(new MediaItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        PostId = postId,
                        FilePath = dest,
                        ThumbnailPath = thumbPath,
                        MediaType = isVid ? "Video" : "Image",
                        CreatedAt = DateTime.Now
                    });
                }
            }
        }

        var post = new Post
        {
            Id = postId,
            AuthorName = string.IsNullOrWhiteSpace(authorName) ? "Offline User" : authorName.Trim(),
            AuthorHandle = string.IsNullOrWhiteSpace(authorHandle) ? "@offline" : (authorHandle.StartsWith("@") ? authorHandle.Trim() : $"@{authorHandle.Trim()}"),
            Content = content ?? string.Empty,
            PostDate = DateTime.Now,
            FolderName = string.IsNullOrWhiteSpace(folder) ? "Coding" : folder.Trim(),
            Tags = tags ?? string.Empty,
            Comment = comment,
            FolderPath = postDirectory,
            CreatedAt = DateTime.Now,
            MediaItems = new ObservableCollection<MediaItem>(mediaItems)
        };

        await WriteFallbackFilesAsync(post, postDirectory);
        await _dbService.InsertPostAsync(post);
        return post;
    }

    private static (string authorName, string authorHandle, string content, string tags) ParseSocialMetadata(string? rawText, int mediaCount)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return ("Offline User", "@offline", mediaCount > 0 ? "Archived Media Snapshot" : "Pasted Note", "");
        }

        rawText = rawText.Trim();

        var lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string authorName = "Offline User";
        string authorHandle = "@offline";
        string content = rawText;

        if (lines.Length >= 2 && lines[1].Trim().StartsWith("@"))
        {
            authorName = lines[0].Trim();
            var handleLine = lines[1].Trim();
            var handleMatch = Regex.Match(handleLine, @"(@[A-Za-z0-9_]+)");
            if (handleMatch.Success)
            {
                authorHandle = handleMatch.Value;
            }

            content = string.Join(Environment.NewLine, lines.Skip(2)).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                content = rawText;
            }
        }
        else
        {
            var handleMatch = Regex.Match(rawText, @"(@[A-Za-z0-9_]+)");
            if (handleMatch.Success)
            {
                authorHandle = handleMatch.Value;
                var cleanName = authorHandle.TrimStart('@');
                authorName = char.ToUpperInvariant(cleanName[0]) + (cleanName.Length > 1 ? cleanName.Substring(1) : "");
            }
        }

        var tagMatches = Regex.Matches(rawText, @"#([a-zA-Z0-9_\-]+)");
        var tags = string.Join(" ", tagMatches.Select(m => m.Value));

        return (authorName, authorHandle, content, tags);
    }

    private static void SaveOptimizedPng(BitmapSource bitmapSource, string destinationPath)
    {
        BitmapSource outputSource = bitmapSource;
        double maxDim = 1920.0;

        if (bitmapSource.PixelWidth > maxDim || bitmapSource.PixelHeight > maxDim)
        {
            double scale = Math.Min(maxDim / bitmapSource.PixelWidth, maxDim / bitmapSource.PixelHeight);
            outputSource = new TransformedBitmap(bitmapSource, new System.Windows.Media.ScaleTransform(scale, scale));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(outputSource));

        using var fileStream = File.Create(destinationPath);
        encoder.Save(fileStream);
    }

    private static async Task WriteFallbackFilesAsync(Post post, string targetDirectory)
    {
        var jsonObj = new
        {
            id = post.Id,
            authorName = post.AuthorName,
            authorHandle = post.AuthorHandle,
            content = post.Content,
            comment = post.Comment,
            postDate = post.PostDate.ToString("o"),
            folderName = post.FolderName,
            tags = post.TagList,
            mediaFiles = post.MediaItems.Select(m => new { name = Path.GetFileName(m.FilePath), type = m.MediaType, path = m.FilePath, thumbnail = m.ThumbnailPath }).ToList()
        };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        string jsonText = JsonSerializer.Serialize(jsonObj, jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(targetDirectory, "post.json"), jsonText, Encoding.UTF8);

        var sb = new StringBuilder();
        sb.AppendLine($"# {post.AuthorName} ({post.AuthorHandle})");
        sb.AppendLine();
        sb.AppendLine($"- **Date:** {post.FormattedDate}");
        sb.AppendLine($"- **Folder:** {post.FolderName}");
        if (post.TagList.Count > 0)
        {
            sb.AppendLine($"- **Tags:** {string.Join(", ", post.TagList.Select(t => $"#{t}"))}");
        }
        if (!string.IsNullOrWhiteSpace(post.Comment))
        {
            sb.AppendLine($"- **Comment / Notes:** {post.Comment}");
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(post.Content);
        sb.AppendLine();

        if (post.MediaItems.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine("### Media Attachments");
            sb.AppendLine();
            foreach (var m in post.MediaItems)
            {
                var fileName = Path.GetFileName(m.FilePath);
                sb.AppendLine($"- **[{m.MediaType}]** [{fileName}]({fileName})");
            }
        }

        await File.WriteAllTextAsync(Path.Combine(targetDirectory, "post.md"), sb.ToString(), Encoding.UTF8);
    }
}

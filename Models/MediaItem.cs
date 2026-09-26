using System.IO;

namespace LocalSocialArchive.Models;

public class MediaItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PostId { get; set; } = string.Empty;
    
    private string _filePath = string.Empty;
    public string FilePath
    {
        get => ResolvePath(_filePath);
        set => _filePath = value ?? string.Empty;
    }

    public string MediaType { get; set; } = "Image";
    public string? ThumbnailPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string FileName => Path.GetFileName(FilePath);

    public bool IsVideo => string.Equals(MediaType, "Video", StringComparison.OrdinalIgnoreCase) || IsVideoExtension(FilePath);
    public bool IsImage => !IsVideo;

    public static bool IsVideoExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi" or ".m4v" or ".wmv" or ".flv";
    }

    private static string ResolvePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
        if (File.Exists(rawPath)) return rawPath;

        // Auto-resolve across different computers, user accounts, and pendrive locations
        try
        {
            string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string baseDir = Path.Combine(userDocs, "LocalSocialArchive");

            // Look for "LocalSocialArchive\" subpath
            int idx = rawPath.IndexOf("LocalSocialArchive", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string rel = rawPath.Substring(idx + "LocalSocialArchive".Length).TrimStart('\\', '/');
                string candidate = Path.Combine(baseDir, rel);
                if (File.Exists(candidate)) return candidate;
            }

            int mediaIdx = rawPath.IndexOf("Media", StringComparison.OrdinalIgnoreCase);
            if (mediaIdx >= 0)
            {
                string relMedia = rawPath.Substring(mediaIdx).TrimStart('\\', '/');
                string candidate = Path.Combine(baseDir, relMedia);
                if (File.Exists(candidate)) return candidate;
            }

            // Match by filename in Media folder
            string fileName = Path.GetFileName(rawPath);
            if (!string.IsNullOrWhiteSpace(fileName) && Directory.Exists(Path.Combine(baseDir, "Media")))
            {
                var matched = Directory.GetFiles(Path.Combine(baseDir, "Media"), fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (matched != null) return matched;
            }
        }
        catch { }

        return rawPath;
    }
}

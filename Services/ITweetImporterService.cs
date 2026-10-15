using LocalSocialArchive.Models;

namespace LocalSocialArchive.Services;

public class ImportProgressReport
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ImportResult
{
    public bool Success { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int MediaCount { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

public interface ITweetImporterService
{
    string? DetectGalleryDlPath();

    Task<ImportResult> ImportTwitterArchiveZipAsync(
        string zipFilePath, 
        string targetFolder = "X Archive", 
        bool importLikes = false, 
        IProgress<ImportProgressReport>? progress = null);

    Task<ImportResult> ImportDirectoryAsync(
        string directoryPath, 
        string targetFolder = "Imported", 
        IProgress<ImportProgressReport>? progress = null);

    Task<ImportResult> DownloadAndImportWithGalleryDlAsync(
        string galleryDlExePath, 
        string targetUrl, 
        string targetFolder = "Downloads", 
        string? browserCookies = null, 
        int maxTweets = 50, 
        IProgress<ImportProgressReport>? progress = null, 
        Action<string>? logOutput = null,
        CancellationToken cancellationToken = default);
}

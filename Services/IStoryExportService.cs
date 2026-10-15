using LocalSocialArchive.Models;

namespace LocalSocialArchive.Services;

public class StoryExportResult
{
    public bool Success { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
    public List<string> ExportedFiles { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public interface IStoryExportService
{
    Task<StoryExportResult> ExportCardsAsync(
        IEnumerable<StoryCard> cards,
        string targetDirectory,
        string baseName,
        IProgress<(int current, int total)>? progress = null);
}

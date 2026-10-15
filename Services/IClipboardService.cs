using LocalSocialArchive.Models;

namespace LocalSocialArchive.Services;

public interface IClipboardService
{
    Task<Post?> ProcessClipboardAsync(string targetFolder = "Coding", bool autoExtractHashtags = true);
    Task<Post?> ProcessFilesAsync(IEnumerable<string> filePaths, string? customContent = null, string targetFolder = "Coding", bool autoExtractHashtags = true);
    Task<Post?> CreateManualPostAsync(
        string authorName,
        string authorHandle,
        string content,
        string folder,
        string tags,
        IEnumerable<string>? mediaPaths = null,
        string? comment = null,
        string? threadId = null,
        int threadIndex = 0);
}

using LocalSocialArchive.Models;

namespace LocalSocialArchive.Services;

public interface IClipboardService
{
    Task<Post?> ProcessClipboardAsync(string targetFolder = "Coding");
    Task<Post?> ProcessFilesAsync(IEnumerable<string> filePaths, string? customContent = null, string targetFolder = "Coding");
    Task<Post?> CreateManualPostAsync(string authorName, string authorHandle, string content, string folder, string tags, IEnumerable<string>? mediaPaths = null, string? comment = null);
}

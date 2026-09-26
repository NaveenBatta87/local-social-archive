using LocalSocialArchive.Models;

namespace LocalSocialArchive.Services;

public interface IEseDatabaseService
{
    string DatabasePath { get; }
    Task InitializeAsync();
    Task<List<Post>> GetAllPostsAsync();
    Task<List<Post>> GetPostsByFolderAsync(string folderName);
    Task<List<Post>> GetPostsByTagAsync(string tagName);
    Task<List<Post>> SearchPostsAsync(string query);
    Task<Post?> GetPostByIdAsync(string postId);
    Task InsertPostAsync(Post post);
    Task UpdatePostAsync(Post post);
    Task DeletePostAsync(string postId);
    Task<long> GetDatabaseSizeBytesAsync();
    Task<Dictionary<string, int>> GetFolderCountsAsync();
    Task<Dictionary<string, int>> GetTagCountsAsync();
    Task SeedSampleDataIfEmptyAsync();
}

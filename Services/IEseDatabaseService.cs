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
    Task<int> InsertPostsBatchAsync(IEnumerable<Post> posts, IProgress<(int current, int total, string status)>? progress = null);
    Task UpdatePostAsync(Post post);
    Task DeletePostAsync(string postId);
    Task DeletePostsAsync(IEnumerable<string> postIds);
    Task SaveCommentsForPostAsync(string postId, IEnumerable<PostComment> comments);
    Task AddCommentToPostAsync(string postId, PostComment comment);
    Task DeleteCommentAsync(string commentId);
    Task<long> GetDatabaseSizeBytesAsync();
    Task<Dictionary<string, int>> GetFolderCountsAsync();
    Task<Dictionary<string, int>> GetTagCountsAsync();
    Task<string> BackupDatabaseAsync(string targetFolderPath);
    Task SeedSampleDataIfEmptyAsync();
}

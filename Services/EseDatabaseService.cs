using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;
using Microsoft.Isam.Esent.Interop;

namespace LocalSocialArchive.Services;

public class EseDatabaseService : IEseDatabaseService, IDisposable
{
    private readonly string _baseDirectory;
    private readonly string _databasePath;
    private readonly string _logDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Instance? _instance;
    private bool _isInitialized;
    private bool _isAttached;

    public string DatabasePath => _databasePath;

    public EseDatabaseService()
    {
        _baseDirectory = StoragePathHelper.BaseDirectory;
        _databasePath = StoragePathHelper.DatabasePath;
        _logDirectory = StoragePathHelper.LogDirectory;

        Directory.CreateDirectory(_baseDirectory);
        Directory.CreateDirectory(_logDirectory);
    }

    public async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            var initTask = Task.Run(() =>
            {
                StoragePathHelper.LogDiagnostic($"Initializing ESE Database. BaseDir: '{_baseDirectory}', DbPath: '{_databasePath}'");
                // Set database page size (must be set before instance initialization)
                SystemParameters.DatabasePageSize = 8192;

                _instance = new Instance("LocalSocialArchiveInstance");
                _instance.Parameters.CreatePathIfNotExist = true;
                _instance.Parameters.CircularLog = true;
                _instance.Parameters.LogFileSize = 512; // 512 KB per log file
                _instance.Parameters.MaxVerPages = 1024;
                _instance.Parameters.SystemDirectory = _logDirectory;
                _instance.Parameters.LogFileDirectory = _logDirectory;
                _instance.Parameters.TempDirectory = _logDirectory;
                _instance.Parameters.Recovery = true;

                try
                {
                    _instance.Init();
                }
                catch (EsentErrorException ex)
                {
                    StoragePathHelper.LogDiagnostic($"[ESE JetInit Warning] {ex.Message}. Attempting log cleanup recovery...");
                    try
                    {
                        string corruptDir = Path.Combine(_logDirectory, $"CorruptLogs_{DateTime.Now:yyyyMMdd_HHmmss}");
                        Directory.CreateDirectory(corruptDir);
                        foreach (var f in Directory.GetFiles(_logDirectory, "*.log"))
                        {
                            try { File.Move(f, Path.Combine(corruptDir, Path.GetFileName(f)), true); } catch { }
                        }
                        foreach (var f in Directory.GetFiles(_logDirectory, "*.jrs"))
                        {
                            try { File.Move(f, Path.Combine(corruptDir, Path.GetFileName(f)), true); } catch { }
                        }
                        _instance.Init();
                    }
                    catch (Exception innerEx)
                    {
                        StoragePathHelper.LogDiagnostic($"[ESE Recovery Failed] {innerEx.Message}");
                        throw;
                    }
                }

                using var session = new Session(_instance);

                if (!File.Exists(_databasePath))
                {
                    StoragePathHelper.LogDiagnostic("Database file not found. Creating new ESE database and schema...");
                    // Create new database (JetCreateDatabase leaves it attached)
                    Api.JetCreateDatabase(session, _databasePath, null, out JET_DBID dbid, CreateDatabaseGrbit.OverwriteExisting);

                    // Create 'Posts' table
                    Api.JetCreateTable(session, dbid, "Posts", 16, 100, out JET_TABLEID postsTableId);
                    Api.JetAddColumn(session, postsTableId, "Id", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "AuthorName", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "AuthorHandle", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "Content", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "PostDate", new JET_COLUMNDEF { coltyp = JET_coltyp.DateTime }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "FolderName", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "Tags", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "FolderPath", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "CreatedAt", new JET_COLUMNDEF { coltyp = JET_coltyp.DateTime }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "ThreadId", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "ThreadIndex", new JET_COLUMNDEF { coltyp = JET_coltyp.Long }, null, 0, out _);
                    Api.JetAddColumn(session, postsTableId, "Comment", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);

                    string pkPosts = "+Id\0\0";
                    Api.JetCreateIndex(session, postsTableId, "PK_Posts", CreateIndexGrbit.IndexPrimary | CreateIndexGrbit.IndexUnique, pkPosts, pkPosts.Length, 100);
                    Api.JetCloseTable(session, postsTableId);

                    // Create 'Media' table
                    Api.JetCreateTable(session, dbid, "Media", 16, 100, out JET_TABLEID mediaTableId);
                    Api.JetAddColumn(session, mediaTableId, "Id", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, mediaTableId, "PostId", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, mediaTableId, "FilePath", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, mediaTableId, "MediaType", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, mediaTableId, "ThumbnailPath", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                    Api.JetAddColumn(session, mediaTableId, "CreatedAt", new JET_COLUMNDEF { coltyp = JET_coltyp.DateTime }, null, 0, out _);

                    string pkMedia = "+Id\0\0";
                    Api.JetCreateIndex(session, mediaTableId, "PK_Media", CreateIndexGrbit.IndexPrimary | CreateIndexGrbit.IndexUnique, pkMedia, pkMedia.Length, 100);

                    string idxPostId = "+PostId\0\0";
                    Api.JetCreateIndex(session, mediaTableId, "IX_Media_PostId", CreateIndexGrbit.None, idxPostId, idxPostId.Length, 100);
                    Api.JetCloseTable(session, mediaTableId);

                    // Create 'Comments' table
                    CreateCommentsTable(session, dbid);

                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                    _isAttached = true;
                    StoragePathHelper.LogDiagnostic("New ESE database created and initial schema established.");
                }
                else
                {
                    StoragePathHelper.LogDiagnostic("Attaching existing ESE database...");
                    // Attach existing database once for instance lifetime
                    try
                    {
                        Api.JetAttachDatabase(session, _databasePath, AttachDatabaseGrbit.None);
                        _isAttached = true;
                        Api.JetOpenDatabase(session, _databasePath, null, out JET_DBID dbid, OpenDatabaseGrbit.None);
                        Api.JetOpenTable(session, dbid, "Posts", null, 0, OpenTableGrbit.None, out JET_TABLEID postsTableId);
                        try
                        {
                            var colDict = Api.GetColumnDictionary(session, postsTableId);
                            if (!colDict.ContainsKey("ThreadId"))
                            {
                                Api.JetAddColumn(session, postsTableId, "ThreadId", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                            }
                            if (!colDict.ContainsKey("ThreadIndex"))
                            {
                                Api.JetAddColumn(session, postsTableId, "ThreadIndex", new JET_COLUMNDEF { coltyp = JET_coltyp.Long }, null, 0, out _);
                            }
                            if (!colDict.ContainsKey("Comment"))
                            {
                                Api.JetAddColumn(session, postsTableId, "Comment", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
                            }
                        }
                        finally
                        {
                            Api.JetCloseTable(session, postsTableId);
                        }

                        // Ensure Comments table exists in existing database
                        try
                        {
                            Api.JetOpenTable(session, dbid, "Comments", null, 0, OpenTableGrbit.None, out JET_TABLEID cTableId);
                            Api.JetCloseTable(session, cTableId);
                        }
                        catch
                        {
                            CreateCommentsTable(session, dbid);
                        }

                        Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                        StoragePathHelper.LogDiagnostic("Existing ESE database attached successfully.");
                    }
                    catch (Exception ex)
                    {
                        StoragePathHelper.LogDiagnostic($"[ESE Open Notice] {ex.Message}");
                        _isAttached = true;
                    }
                }

                _isInitialized = true;
            });

            var completedTask = await Task.WhenAny(initTask, Task.Delay(TimeSpan.FromSeconds(20)));
            if (completedTask != initTask)
            {
                StoragePathHelper.LogDiagnostic("[CRITICAL] ESE database initialization timed out after 20 seconds.");
                throw new TimeoutException("Database initialization timed out. Please verify disk access permissions.");
            }

            await initTask;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task InsertPostAsync(Post post)
    {
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (_instance == null) throw new InvalidOperationException("ESE Database is not initialized.");

                using var session = new Session(_instance);
                Api.JetOpenDatabase(session, _databasePath, null, out JET_DBID dbid, OpenDatabaseGrbit.None);

                try
                {
                    // 1. Insert Post Record
                    Api.JetOpenTable(session, dbid, "Posts", null, 0, OpenTableGrbit.None, out JET_TABLEID postsTableId);
                    try
                    {
                        var colDict = Api.GetColumnDictionary(session, postsTableId);
                        using (var tx = new Transaction(session))
                        {
                            using (var update = new Update(session, postsTableId, JET_prep.Insert))
                            {
                                Api.SetColumn(session, postsTableId, colDict["Id"], post.Id, Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["AuthorName"], post.AuthorName ?? "Offline User", Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["AuthorHandle"], post.AuthorHandle ?? "@offline", Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["Content"], post.Content ?? string.Empty, Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["PostDate"], post.PostDate);
                                Api.SetColumn(session, postsTableId, colDict["FolderName"], post.FolderName ?? "Coding", Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["Tags"], post.Tags ?? string.Empty, Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["FolderPath"], post.FolderPath ?? string.Empty, Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["CreatedAt"], post.CreatedAt);
                                if (colDict.ContainsKey("ThreadId") && !string.IsNullOrWhiteSpace(post.ThreadId))
                                {
                                    Api.SetColumn(session, postsTableId, colDict["ThreadId"], post.ThreadId, Encoding.Unicode);
                                }
                                if (colDict.ContainsKey("ThreadIndex"))
                                {
                                    Api.SetColumn(session, postsTableId, colDict["ThreadIndex"], post.ThreadIndex);
                                }
                                if (colDict.ContainsKey("Comment"))
                                {
                                    Api.SetColumn(session, postsTableId, colDict["Comment"], post.Comment ?? string.Empty, Encoding.Unicode);
                                }
                                update.Save();
                            }
                            tx.Commit(CommitTransactionGrbit.LazyFlush);
                        }
                    }
                    finally
                    {
                        Api.JetCloseTable(session, postsTableId);
                    }

                    // 2. Insert Media Records
                    if (post.MediaItems != null && post.MediaItems.Count > 0)
                    {
                        Api.JetOpenTable(session, dbid, "Media", null, 0, OpenTableGrbit.None, out JET_TABLEID mediaTableId);
                        try
                        {
                            var mediaColDict = Api.GetColumnDictionary(session, mediaTableId);
                            using (var tx = new Transaction(session))
                            {
                                foreach (var media in post.MediaItems)
                                {
                                    using var update = new Update(session, mediaTableId, JET_prep.Insert);
                                    Api.SetColumn(session, mediaTableId, mediaColDict["Id"], media.Id, Encoding.Unicode);
                                    Api.SetColumn(session, mediaTableId, mediaColDict["PostId"], post.Id, Encoding.Unicode);
                                    Api.SetColumn(session, mediaTableId, mediaColDict["FilePath"], media.FilePath ?? string.Empty, Encoding.Unicode);
                                    Api.SetColumn(session, mediaTableId, mediaColDict["MediaType"], media.MediaType ?? "Image", Encoding.Unicode);
                                    Api.SetColumn(session, mediaTableId, mediaColDict["ThumbnailPath"], media.ThumbnailPath ?? string.Empty, Encoding.Unicode);
                                    Api.SetColumn(session, mediaTableId, mediaColDict["CreatedAt"], media.CreatedAt);
                                    update.Save();
                                }
                                tx.Commit(CommitTransactionGrbit.LazyFlush);
                            }
                        }
                        finally
                        {
                            Api.JetCloseTable(session, mediaTableId);
                        }
                    }

                    // 3. Insert Comments Records
                    if (post.Comments != null && post.Comments.Count > 0)
                    {
                        InsertCommentsInternal(session, dbid, post.Id, post.Comments);
                    }
                }
                finally
                {
                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                }
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> InsertPostsBatchAsync(IEnumerable<Post> posts, IProgress<(int current, int total, string status)>? progress = null)
    {
        var postList = posts.Where(p => p != null).ToList();
        if (postList.Count == 0) return 0;

        await _lock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                if (_instance == null) throw new InvalidOperationException("ESE Database is not initialized.");

                int insertedCount = 0;
                int total = postList.Count;

                using var session = new Session(_instance);
                Api.JetOpenDatabase(session, _databasePath, null, out JET_DBID dbid, OpenDatabaseGrbit.None);

                try
                {
                    Api.JetOpenTable(session, dbid, "Posts", null, 0, OpenTableGrbit.None, out JET_TABLEID postsTableId);
                    Api.JetOpenTable(session, dbid, "Media", null, 0, OpenTableGrbit.None, out JET_TABLEID mediaTableId);

                    try
                    {
                        var postColDict = Api.GetColumnDictionary(session, postsTableId);
                        var mediaColDict = Api.GetColumnDictionary(session, mediaTableId);

                        const int batchSize = 100;
                        for (int i = 0; i < total; i += batchSize)
                        {
                            var chunk = postList.Skip(i).Take(batchSize).ToList();
                            using (var tx = new Transaction(session))
                            {
                                foreach (var post in chunk)
                                {
                                    // Check if post already exists to avoid primary key collisions
                                    Api.JetSetCurrentIndex(session, postsTableId, "PK_Posts");
                                    Api.MakeKey(session, postsTableId, post.Id, Encoding.Unicode, MakeKeyGrbit.NewKey);
                                    if (Api.TrySeek(session, postsTableId, SeekGrbit.SeekEQ))
                                    {
                                        continue; // skip duplicate
                                    }

                                    using (var update = new Update(session, postsTableId, JET_prep.Insert))
                                    {
                                        Api.SetColumn(session, postsTableId, postColDict["Id"], post.Id, Encoding.Unicode);
                                        Api.SetColumn(session, postsTableId, postColDict["AuthorName"], post.AuthorName ?? "Offline User", Encoding.Unicode);
                                        Api.SetColumn(session, postsTableId, postColDict["AuthorHandle"], post.AuthorHandle ?? "@offline", Encoding.Unicode);
                                        Api.SetColumn(session, postsTableId, postColDict["Content"], post.Content ?? string.Empty, Encoding.Unicode);
                                        Api.SetColumn(session, postsTableId, postColDict["PostDate"], post.PostDate);
                                        Api.SetColumn(session, postsTableId, postColDict["FolderName"], post.FolderName ?? "Coding", Encoding.Unicode);
                                        Api.SetColumn(session, postsTableId, postColDict["Tags"], post.Tags ?? string.Empty, Encoding.Unicode);
                                        Api.SetColumn(session, postsTableId, postColDict["FolderPath"], post.FolderPath ?? string.Empty, Encoding.Unicode);
                                        Api.SetColumn(session, postsTableId, postColDict["CreatedAt"], post.CreatedAt);
                                        if (postColDict.ContainsKey("ThreadId") && !string.IsNullOrWhiteSpace(post.ThreadId))
                                        {
                                            Api.SetColumn(session, postsTableId, postColDict["ThreadId"], post.ThreadId, Encoding.Unicode);
                                        }
                                        if (postColDict.ContainsKey("ThreadIndex"))
                                        {
                                            Api.SetColumn(session, postsTableId, postColDict["ThreadIndex"], post.ThreadIndex);
                                        }
                                        if (postColDict.ContainsKey("Comment"))
                                        {
                                            Api.SetColumn(session, postsTableId, postColDict["Comment"], post.Comment ?? string.Empty, Encoding.Unicode);
                                        }
                                        update.Save();
                                    }

                                    if (post.MediaItems != null && post.MediaItems.Count > 0)
                                    {
                                        foreach (var media in post.MediaItems)
                                        {
                                            using var mediaUpdate = new Update(session, mediaTableId, JET_prep.Insert);
                                            Api.SetColumn(session, mediaTableId, mediaColDict["Id"], media.Id, Encoding.Unicode);
                                            Api.SetColumn(session, mediaTableId, mediaColDict["PostId"], post.Id, Encoding.Unicode);
                                            Api.SetColumn(session, mediaTableId, mediaColDict["FilePath"], media.FilePath ?? string.Empty, Encoding.Unicode);
                                            Api.SetColumn(session, mediaTableId, mediaColDict["MediaType"], media.MediaType ?? "Image", Encoding.Unicode);
                                            Api.SetColumn(session, mediaTableId, mediaColDict["ThumbnailPath"], media.ThumbnailPath ?? string.Empty, Encoding.Unicode);
                                            Api.SetColumn(session, mediaTableId, mediaColDict["CreatedAt"], media.CreatedAt);
                                            mediaUpdate.Save();
                                        }
                                    }

                                    if (post.Comments != null && post.Comments.Count > 0)
                                    {
                                        InsertCommentsInternal(session, dbid, post.Id, post.Comments);
                                    }

                                    insertedCount++;
                                }

                                tx.Commit(CommitTransactionGrbit.LazyFlush);
                            }

                            progress?.Report((insertedCount, total, $"Imported {insertedCount} of {total} posts..."));
                        }
                    }
                    finally
                    {
                        Api.JetCloseTable(session, postsTableId);
                        Api.JetCloseTable(session, mediaTableId);
                    }
                }
                finally
                {
                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                }

                return insertedCount;
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<Post>> GetAllPostsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                if (_instance == null) return new List<Post>();

                var posts = new List<Post>();
                var mediaByPostId = new Dictionary<string, List<MediaItem>>();

                using var session = new Session(_instance);
                Api.JetOpenDatabase(session, _databasePath, null, out JET_DBID dbid, OpenDatabaseGrbit.None);

                try
                {
                    // Load Media
                    Api.JetOpenTable(session, dbid, "Media", null, 0, OpenTableGrbit.None, out JET_TABLEID mediaTableId);
                    try
                    {
                        var mediaColDict = Api.GetColumnDictionary(session, mediaTableId);
                        if (Api.TryMoveFirst(session, mediaTableId))
                        {
                            do
                            {
                                var item = new MediaItem
                                {
                                    Id = Api.RetrieveColumnAsString(session, mediaTableId, mediaColDict["Id"], Encoding.Unicode) ?? Guid.NewGuid().ToString("N"),
                                    PostId = Api.RetrieveColumnAsString(session, mediaTableId, mediaColDict["PostId"], Encoding.Unicode) ?? string.Empty,
                                    FilePath = Api.RetrieveColumnAsString(session, mediaTableId, mediaColDict["FilePath"], Encoding.Unicode) ?? string.Empty,
                                    MediaType = Api.RetrieveColumnAsString(session, mediaTableId, mediaColDict["MediaType"], Encoding.Unicode) ?? "Image",
                                    ThumbnailPath = Api.RetrieveColumnAsString(session, mediaTableId, mediaColDict["ThumbnailPath"], Encoding.Unicode),
                                    CreatedAt = Api.RetrieveColumnAsDateTime(session, mediaTableId, mediaColDict["CreatedAt"]) ?? DateTime.Now
                                };

                                if (!mediaByPostId.TryGetValue(item.PostId, out var mList))
                                {
                                    mList = new List<MediaItem>();
                                    mediaByPostId[item.PostId] = mList;
                                }
                                mList.Add(item);
                            } while (Api.TryMoveNext(session, mediaTableId));
                        }
                    }
                    finally
                    {
                        Api.JetCloseTable(session, mediaTableId);
                    }

                    // Load Comments
                    var commentsByPostId = new Dictionary<string, List<PostComment>>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        Api.JetOpenTable(session, dbid, "Comments", null, 0, OpenTableGrbit.None, out JET_TABLEID commentsTableId);
                        try
                        {
                            var cColDict = Api.GetColumnDictionary(session, commentsTableId);
                            if (Api.TryMoveFirst(session, commentsTableId))
                            {
                                do
                                {
                                    var item = new PostComment
                                    {
                                        Id = Api.RetrieveColumnAsString(session, commentsTableId, cColDict["Id"], Encoding.Unicode) ?? Guid.NewGuid().ToString("N"),
                                        PostId = Api.RetrieveColumnAsString(session, commentsTableId, cColDict["PostId"], Encoding.Unicode) ?? string.Empty,
                                        AuthorName = Api.RetrieveColumnAsString(session, commentsTableId, cColDict["AuthorName"], Encoding.Unicode) ?? string.Empty,
                                        AuthorHandle = Api.RetrieveColumnAsString(session, commentsTableId, cColDict["AuthorHandle"], Encoding.Unicode) ?? string.Empty,
                                        Content = Api.RetrieveColumnAsString(session, commentsTableId, cColDict["Content"], Encoding.Unicode) ?? string.Empty,
                                        TimeAgo = cColDict.ContainsKey("TimeAgo") ? (Api.RetrieveColumnAsString(session, commentsTableId, cColDict["TimeAgo"], Encoding.Unicode) ?? string.Empty) : string.Empty,
                                        CreatedAt = Api.RetrieveColumnAsDateTime(session, commentsTableId, cColDict["CreatedAt"]) ?? DateTime.Now,
                                        LikeCount = cColDict.ContainsKey("LikeCount") ? (Api.RetrieveColumnAsString(session, commentsTableId, cColDict["LikeCount"], Encoding.Unicode) ?? string.Empty) : string.Empty,
                                        ReplyCount = cColDict.ContainsKey("ReplyCount") ? (Api.RetrieveColumnAsString(session, commentsTableId, cColDict["ReplyCount"], Encoding.Unicode) ?? string.Empty) : string.Empty
                                    };

                                    if (!commentsByPostId.TryGetValue(item.PostId, out var cList))
                                    {
                                        cList = new List<PostComment>();
                                        commentsByPostId[item.PostId] = cList;
                                    }
                                    cList.Add(item);
                                } while (Api.TryMoveNext(session, commentsTableId));
                            }
                        }
                        finally
                        {
                            Api.JetCloseTable(session, commentsTableId);
                        }
                    }
                    catch { }

                    // Load Posts
                    Api.JetOpenTable(session, dbid, "Posts", null, 0, OpenTableGrbit.None, out JET_TABLEID postsTableId);
                    try
                    {
                        var colDict = Api.GetColumnDictionary(session, postsTableId);
                        if (Api.TryMoveFirst(session, postsTableId))
                        {
                            do
                            {
                                var post = new Post
                                {
                                    Id = Api.RetrieveColumnAsString(session, postsTableId, colDict["Id"], Encoding.Unicode) ?? Guid.NewGuid().ToString("N"),
                                    AuthorName = Api.RetrieveColumnAsString(session, postsTableId, colDict["AuthorName"], Encoding.Unicode) ?? "Offline User",
                                    AuthorHandle = Api.RetrieveColumnAsString(session, postsTableId, colDict["AuthorHandle"], Encoding.Unicode) ?? "@offline",
                                    Content = Api.RetrieveColumnAsString(session, postsTableId, colDict["Content"], Encoding.Unicode) ?? string.Empty,
                                    PostDate = Api.RetrieveColumnAsDateTime(session, postsTableId, colDict["PostDate"]) ?? DateTime.Now,
                                    FolderName = Api.RetrieveColumnAsString(session, postsTableId, colDict["FolderName"], Encoding.Unicode) ?? "Coding",
                                    Tags = Api.RetrieveColumnAsString(session, postsTableId, colDict["Tags"], Encoding.Unicode) ?? string.Empty,
                                    FolderPath = Api.RetrieveColumnAsString(session, postsTableId, colDict["FolderPath"], Encoding.Unicode) ?? string.Empty,
                                    CreatedAt = Api.RetrieveColumnAsDateTime(session, postsTableId, colDict["CreatedAt"]) ?? DateTime.Now,
                                    ThreadId = colDict.ContainsKey("ThreadId") ? Api.RetrieveColumnAsString(session, postsTableId, colDict["ThreadId"], Encoding.Unicode) : null,
                                    ThreadIndex = colDict.ContainsKey("ThreadIndex") ? (Api.RetrieveColumnAsInt32(session, postsTableId, colDict["ThreadIndex"]) ?? 0) : 0,
                                    Comment = colDict.ContainsKey("Comment") ? Api.RetrieveColumnAsString(session, postsTableId, colDict["Comment"], Encoding.Unicode) : null
                                };

                                if (mediaByPostId.TryGetValue(post.Id, out var mediaList))
                                {
                                    post.MediaItems = new ObservableCollection<MediaItem>(mediaList);
                                }

                                if (commentsByPostId.TryGetValue(post.Id, out var commentList))
                                {
                                    post.Comments = new ObservableCollection<PostComment>(commentList);
                                }

                                posts.Add(post);
                            } while (Api.TryMoveNext(session, postsTableId));
                        }
                    }
                    finally
                    {
                        Api.JetCloseTable(session, postsTableId);
                    }
                }
                finally
                {
                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                }

                // Return sorted descending by PostDate
                return posts.OrderByDescending(p => p.PostDate).ToList();
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<Post>> GetPostsByFolderAsync(string folderName)
    {
        var all = await GetAllPostsAsync();
        if (string.Equals(folderName, "All Posts", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(folderName))
        {
            return all;
        }

        return all.Where(p => string.Equals(p.FolderName, folderName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<List<Post>> GetPostsByTagAsync(string tagName)
    {
        var cleanTag = tagName.TrimStart('#').ToLowerInvariant();
        var all = await GetAllPostsAsync();
        return all.Where(p => p.TagList.Any(t => string.Equals(t, cleanTag, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public async Task<List<Post>> SearchPostsAsync(string query)
    {
        var all = await GetAllPostsAsync();
        if (string.IsNullOrWhiteSpace(query))
        {
            return all;
        }

        var q = query.Trim().ToLowerInvariant();
        return all.Where(p =>
            p.Content.ToLowerInvariant().Contains(q) ||
            p.AuthorName.ToLowerInvariant().Contains(q) ||
            p.AuthorHandle.ToLowerInvariant().Contains(q) ||
            p.FolderName.ToLowerInvariant().Contains(q) ||
            (p.Comment != null && p.Comment.ToLowerInvariant().Contains(q)) ||
            p.Comments.Any(c => c.Content.ToLowerInvariant().Contains(q) || c.AuthorHandle.ToLowerInvariant().Contains(q) || c.AuthorName.ToLowerInvariant().Contains(q)) ||
            p.TagList.Any(t => t.ToLowerInvariant().Contains(q))
        ).ToList();
    }

    public async Task<Post?> GetPostByIdAsync(string postId)
    {
        var all = await GetAllPostsAsync();
        return all.FirstOrDefault(p => p.Id == postId);
    }

    public async Task UpdatePostAsync(Post post)
    {
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (_instance == null) throw new InvalidOperationException("ESE Database is not initialized.");

                using var session = new Session(_instance);
                Api.JetOpenDatabase(session, _databasePath, null, out JET_DBID dbid, OpenDatabaseGrbit.None);

                try
                {
                    Api.JetOpenTable(session, dbid, "Posts", null, 0, OpenTableGrbit.None, out JET_TABLEID postsTableId);
                    try
                    {
                        var colDict = Api.GetColumnDictionary(session, postsTableId);
                        Api.JetSetCurrentIndex(session, postsTableId, "PK_Posts");
                        Api.MakeKey(session, postsTableId, post.Id, Encoding.Unicode, MakeKeyGrbit.NewKey);

                        if (Api.TrySeek(session, postsTableId, SeekGrbit.SeekEQ))
                        {
                            using var tx = new Transaction(session);
                            using (var update = new Update(session, postsTableId, JET_prep.Replace))
                            {
                                Api.SetColumn(session, postsTableId, colDict["AuthorName"], post.AuthorName, Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["AuthorHandle"], post.AuthorHandle, Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["Content"], post.Content, Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["FolderName"], post.FolderName, Encoding.Unicode);
                                Api.SetColumn(session, postsTableId, colDict["Tags"], post.Tags, Encoding.Unicode);
                                if (colDict.ContainsKey("ThreadId"))
                                {
                                    Api.SetColumn(session, postsTableId, colDict["ThreadId"], post.ThreadId ?? string.Empty, Encoding.Unicode);
                                }
                                if (colDict.ContainsKey("ThreadIndex"))
                                {
                                    Api.SetColumn(session, postsTableId, colDict["ThreadIndex"], post.ThreadIndex);
                                }
                                if (colDict.ContainsKey("Comment"))
                                {
                                    Api.SetColumn(session, postsTableId, colDict["Comment"], post.Comment ?? string.Empty, Encoding.Unicode);
                                }
                                update.Save();
                            }
                            tx.Commit(CommitTransactionGrbit.LazyFlush);
                        }
                    }
                    finally
                    {
                        Api.JetCloseTable(session, postsTableId);
                    }

                    // 2. Synchronize Media Records for this Post
                    Api.JetOpenTable(session, dbid, "Media", null, 0, OpenTableGrbit.None, out JET_TABLEID mediaTableId);
                    try
                    {
                        var mediaColDict = Api.GetColumnDictionary(session, mediaTableId);
                        
                        // A. Delete existing media records for this PostId
                        if (Api.TryMoveFirst(session, mediaTableId))
                        {
                            do
                            {
                                var currentPostId = Api.RetrieveColumnAsString(session, mediaTableId, mediaColDict["PostId"], Encoding.Unicode);
                                if (string.Equals(currentPostId, post.Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    using var txMedia = new Transaction(session);
                                    Api.JetDelete(session, mediaTableId);
                                    txMedia.Commit(CommitTransactionGrbit.LazyFlush);
                                }
                            } while (Api.TryMoveNext(session, mediaTableId));
                        }

                        // B. Re-insert current media items
                        if (post.MediaItems != null && post.MediaItems.Count > 0)
                        {
                            using var txMedia = new Transaction(session);
                            foreach (var media in post.MediaItems)
                            {
                                using var update = new Update(session, mediaTableId, JET_prep.Insert);
                                Api.SetColumn(session, mediaTableId, mediaColDict["Id"], media.Id, Encoding.Unicode);
                                Api.SetColumn(session, mediaTableId, mediaColDict["PostId"], post.Id, Encoding.Unicode);
                                Api.SetColumn(session, mediaTableId, mediaColDict["FilePath"], media.FilePath ?? string.Empty, Encoding.Unicode);
                                Api.SetColumn(session, mediaTableId, mediaColDict["MediaType"], media.MediaType ?? "Image", Encoding.Unicode);
                                Api.SetColumn(session, mediaTableId, mediaColDict["ThumbnailPath"], media.ThumbnailPath ?? string.Empty, Encoding.Unicode);
                                Api.SetColumn(session, mediaTableId, mediaColDict["CreatedAt"], media.CreatedAt);
                                update.Save();
                            }
                            txMedia.Commit(CommitTransactionGrbit.LazyFlush);
                        }
                    }
                    finally
                    {
                        Api.JetCloseTable(session, mediaTableId);
                    }

                    // 3. Synchronize Comments Records for this Post
                    SynchronizeCommentsInternal(session, dbid, post.Id, post.Comments);
                }
                finally
                {
                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                }
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeletePostAsync(string postId)
    {
        await DeletePostsAsync(new[] { postId });
    }

    public async Task DeletePostsAsync(IEnumerable<string> postIds)
    {
        var idList = postIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (idList.Count == 0) return;
        var idSet = new HashSet<string>(idList, StringComparer.OrdinalIgnoreCase);

        await _lock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (_instance == null) throw new InvalidOperationException("ESE Database is not initialized.");

                using var session = new Session(_instance);
                Api.JetOpenDatabase(session, _databasePath, null, out JET_DBID dbid, OpenDatabaseGrbit.None);

                try
                {
                    // 1. Delete Post
                    Api.JetOpenTable(session, dbid, "Posts", null, 0, OpenTableGrbit.None, out JET_TABLEID postsTableId);
                    try
                    {
                        Api.JetSetCurrentIndex(session, postsTableId, "PK_Posts");
                        foreach (var id in idSet)
                        {
                            Api.MakeKey(session, postsTableId, id, Encoding.Unicode, MakeKeyGrbit.NewKey);
                            if (Api.TrySeek(session, postsTableId, SeekGrbit.SeekEQ))
                            {
                                using var tx = new Transaction(session);
                                Api.JetDelete(session, postsTableId);
                                tx.Commit(CommitTransactionGrbit.LazyFlush);
                            }
                        }
                    }
                    finally
                    {
                        Api.JetCloseTable(session, postsTableId);
                    }

                    // 2. Delete Media records associated with this PostId
                    Api.JetOpenTable(session, dbid, "Media", null, 0, OpenTableGrbit.None, out JET_TABLEID mediaTableId);
                    try
                    {
                        var mediaColDict = Api.GetColumnDictionary(session, mediaTableId);
                        if (Api.TryMoveFirst(session, mediaTableId))
                        {
                            do
                            {
                                var currentPostId = Api.RetrieveColumnAsString(session, mediaTableId, mediaColDict["PostId"], Encoding.Unicode);
                                if (!string.IsNullOrEmpty(currentPostId) && idSet.Contains(currentPostId))
                                {
                                    using var tx = new Transaction(session);
                                    Api.JetDelete(session, mediaTableId);
                                    tx.Commit(CommitTransactionGrbit.LazyFlush);
                                }
                            } while (Api.TryMoveNext(session, mediaTableId));
                        }
                    }
                    finally
                    {
                        Api.JetCloseTable(session, mediaTableId);
                    }

                    // 3. Delete Comments records associated with these PostIds
                    DeleteCommentsByPostIdsInternal(session, dbid, idSet);
                }
                finally
                {
                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                }
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<long> GetDatabaseSizeBytesAsync()
    {
        return await Task.Run(() =>
        {
            long totalSize = 0;
            if (File.Exists(_databasePath))
            {
                totalSize += new FileInfo(_databasePath).Length;
            }

            if (Directory.Exists(_logDirectory))
            {
                var files = Directory.GetFiles(_logDirectory);
                foreach (var f in files)
                {
                    totalSize += new FileInfo(f).Length;
                }
            }
            return totalSize;
        });
    }

    public async Task<Dictionary<string, int>> GetFolderCountsAsync()
    {
        var all = await GetAllPostsAsync();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["All Posts"] = all.Count,
            ["Coding"] = 0,
            ["SAP Projects"] = 0,
            ["Memes"] = 0,
            ["Reference"] = 0
        };

        foreach (var p in all)
        {
            var folder = string.IsNullOrWhiteSpace(p.FolderName) ? "Coding" : p.FolderName;
            if (counts.ContainsKey(folder))
            {
                counts[folder]++;
            }
            else
            {
                counts[folder] = 1;
            }
        }

        return counts;
    }

    public async Task<Dictionary<string, int>> GetTagCountsAsync()
    {
        var all = await GetAllPostsAsync();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in all)
        {
            foreach (var tag in p.TagList)
            {
                if (counts.ContainsKey(tag))
                {
                    counts[tag]++;
                }
                else
                {
                    counts[tag] = 1;
                }
            }
        }

        return counts;
    }

    public async Task SeedSampleDataIfEmptyAsync()
    {
        var all = await GetAllPostsAsync();
        if (all.Count > 0) return;

        // Create sample media directory
        string sampleMediaDir1 = Path.Combine(_baseDirectory, "Media", $"{DateTime.Now:yyyyMMdd}_demo_sap");
        string sampleMediaDir2 = Path.Combine(_baseDirectory, "Media", $"{DateTime.Now:yyyyMMdd}_demo_guru");
        Directory.CreateDirectory(sampleMediaDir1);
        Directory.CreateDirectory(sampleMediaDir2);

        // Generate synthetic preview images for high-fidelity demonstration matching the screenshot
        string imgPath1 = Path.Combine(sampleMediaDir1, "preview_windows.png");
        string imgPath2 = Path.Combine(sampleMediaDir1, "preview_architecture.png");
        
        CreateSampleGradientBitmap(imgPath1, "Image Preview 1", "#0078D4", "#111A24");
        CreateSampleGradientBitmap(imgPath2, "Image Preview 2", "#1D9BF0", "#0D1117");

        var samplePost1 = new Post
        {
            Id = Guid.NewGuid().ToString("N"),
            AuthorName = "SAP Developers",
            AuthorHandle = "@SAPDevs",
            Content = "Clean architecture example for Windows native desktop apps using C# and ESE engine. Perfect for ultra-fast offline indexing!\n#winui3 #sqlite #csharp",
            PostDate = DateTime.Now.AddHours(-2),
            FolderName = "SAP Projects",
            Tags = "#sap #winui3 #sqlite #csharp",
            FolderPath = sampleMediaDir1,
            CreatedAt = DateTime.Now.AddHours(-2),
            MediaItems = new ObservableCollection<MediaItem>
            {
                new MediaItem { FilePath = imgPath1, MediaType = "Image" },
                new MediaItem { FilePath = imgPath2, MediaType = "Image" }
            }
        };

        var samplePost2 = new Post
        {
            Id = Guid.NewGuid().ToString("N"),
            AuthorName = "Tech Guru",
            AuthorHandle = "@TechGuru",
            Content = "Pointers in C# enable high-performance memory manipulation without high Garbage Collection overhead. #csharp\n#performance",
            PostDate = DateTime.Now.AddDays(-1),
            FolderName = "Coding",
            Tags = "#csharp #performance",
            FolderPath = sampleMediaDir2,
            CreatedAt = DateTime.Now.AddDays(-1)
        };

        var samplePost3 = new Post
        {
            Id = Guid.NewGuid().ToString("N"),
            AuthorName = "WPF Master",
            AuthorHandle = "@wpf_enthusiast",
            Content = "VirtualizingStackPanel with recycling mode delivers silky smooth 120fps scrolling on massive desktop feeds. Native Windows UI still reigns supreme for offline productivity apps! 🚀 #csharp #winui3",
            PostDate = DateTime.Now.AddDays(-2),
            FolderName = "Reference",
            Tags = "#csharp #winui3 #wpf",
            FolderPath = sampleMediaDir1,
            CreatedAt = DateTime.Now.AddDays(-2)
        };

        samplePost1.Comments = new ObservableCollection<PostComment>
        {
            new PostComment
            {
                PostId = samplePost1.Id,
                AuthorHandle = "@RickeGnool",
                AuthorName = "RickeGnool",
                TimeAgo = "2 years ago",
                Content = "While it's sad that Aang died when he was just in his 60s I try to look at it more positively:\nHe and Katara had about 50 years together as a happy couple, which is alot.",
                LikeCount = "843",
                ReplyCount = "15 replies"
            },
            new PostComment
            {
                PostId = samplePost1.Id,
                AuthorHandle = "@ladydjiv",
                AuthorName = "ladydjiv",
                TimeAgo = "2 years ago",
                Content = "You know what, I love the small detail of Korra treating the kids like grandkids. She plays with them, listens to them, and communicates with them on that weird grandparent level. And the kids treat her like that too. She's not just a big sister, they listen to her, and do what she says. It's a reminder of that connection to Aang, and that he isn't really gone. The kids still have their granddad around. It's such a nice connecting detail~",
                LikeCount = "1.2K",
                ReplyCount = "17 replies"
            },
            new PostComment
            {
                PostId = samplePost1.Id,
                AuthorHandle = "@chheinrich8486",
                AuthorName = "chheinrich8486",
                TimeAgo = "3 years ago (edited)",
                Content = "Love how half this shows side characters are either the beifong family or aangs family",
                LikeCount = "2.5K",
                ReplyCount = "14 replies"
            },
            new PostComment
            {
                PostId = samplePost1.Id,
                AuthorHandle = "@spalenzagabriel",
                AuthorName = "spalenzagabriel",
                TimeAgo = "2 years ago",
                Content = "All of Aang's kids are perfect. Each representing one of the original trio. Tenzin the responsible and explosive like Katara, Bumi the smart and goofy like Sokka and Kya the calm, collected, sweet and fierce like Aang.",
                LikeCount = "450",
                ReplyCount = "8 replies"
            }
        };

        await InsertPostAsync(samplePost1);
        await InsertPostAsync(samplePost2);
        await InsertPostAsync(samplePost3);
    }

    public async Task SaveCommentsForPostAsync(string postId, IEnumerable<PostComment> comments)
    {
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (_instance == null) throw new InvalidOperationException("ESE Database is not initialized.");
                using var session = new Session(_instance);
                Api.JetOpenDatabase(session, _databasePath, null, out JET_DBID dbid, OpenDatabaseGrbit.None);
                try
                {
                    SynchronizeCommentsInternal(session, dbid, postId, comments);
                }
                finally
                {
                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                }
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddCommentToPostAsync(string postId, PostComment comment)
    {
        comment.PostId = postId;
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (_instance == null) throw new InvalidOperationException("ESE Database is not initialized.");
                using var session = new Session(_instance);
                Api.JetOpenDatabase(session, _databasePath, null, out JET_DBID dbid, OpenDatabaseGrbit.None);
                try
                {
                    InsertSingleCommentInternal(session, dbid, comment);
                }
                finally
                {
                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                }
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteCommentAsync(string commentId)
    {
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (_instance == null) throw new InvalidOperationException("ESE Database is not initialized.");
                using var session = new Session(_instance);
                Api.JetOpenDatabase(session, _databasePath, null, out JET_DBID dbid, OpenDatabaseGrbit.None);
                try
                {
                    Api.JetOpenTable(session, dbid, "Comments", null, 0, OpenTableGrbit.None, out JET_TABLEID cTableId);
                    try
                    {
                        Api.JetSetCurrentIndex(session, cTableId, "PK_Comments");
                        Api.MakeKey(session, cTableId, commentId, Encoding.Unicode, MakeKeyGrbit.NewKey);
                        if (Api.TrySeek(session, cTableId, SeekGrbit.SeekEQ))
                        {
                            using var tx = new Transaction(session);
                            Api.JetDelete(session, cTableId);
                            tx.Commit(CommitTransactionGrbit.LazyFlush);
                        }
                    }
                    finally
                    {
                        Api.JetCloseTable(session, cTableId);
                    }
                }
                finally
                {
                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                }
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    private static void CreateCommentsTable(Session session, JET_DBID dbid)
    {
        try
        {
            Api.JetCreateTable(session, dbid, "Comments", 16, 100, out JET_TABLEID commentsTableId);
            Api.JetAddColumn(session, commentsTableId, "Id", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
            Api.JetAddColumn(session, commentsTableId, "PostId", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
            Api.JetAddColumn(session, commentsTableId, "AuthorName", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
            Api.JetAddColumn(session, commentsTableId, "AuthorHandle", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
            Api.JetAddColumn(session, commentsTableId, "Content", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
            Api.JetAddColumn(session, commentsTableId, "TimeAgo", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
            Api.JetAddColumn(session, commentsTableId, "CreatedAt", new JET_COLUMNDEF { coltyp = JET_coltyp.DateTime }, null, 0, out _);
            Api.JetAddColumn(session, commentsTableId, "LikeCount", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);
            Api.JetAddColumn(session, commentsTableId, "ReplyCount", new JET_COLUMNDEF { coltyp = JET_coltyp.LongText, cp = JET_CP.Unicode }, null, 0, out _);

            string pkComments = "+Id\0\0";
            Api.JetCreateIndex(session, commentsTableId, "PK_Comments", CreateIndexGrbit.IndexPrimary | CreateIndexGrbit.IndexUnique, pkComments, pkComments.Length, 100);

            string idxPostId = "+PostId\0\0";
            Api.JetCreateIndex(session, commentsTableId, "IX_Comments_PostId", CreateIndexGrbit.None, idxPostId, idxPostId.Length, 100);
            Api.JetCloseTable(session, commentsTableId);
        }
        catch { }
    }

    private static void InsertCommentsInternal(Session session, JET_DBID dbid, string postId, IEnumerable<PostComment> comments)
    {
        try
        {
            Api.JetOpenTable(session, dbid, "Comments", null, 0, OpenTableGrbit.None, out JET_TABLEID commentsTableId);
            try
            {
                var cColDict = Api.GetColumnDictionary(session, commentsTableId);
                using var tx = new Transaction(session);
                foreach (var c in comments)
                {
                    using var update = new Update(session, commentsTableId, JET_prep.Insert);
                    Api.SetColumn(session, commentsTableId, cColDict["Id"], c.Id, Encoding.Unicode);
                    Api.SetColumn(session, commentsTableId, cColDict["PostId"], postId, Encoding.Unicode);
                    Api.SetColumn(session, commentsTableId, cColDict["AuthorName"], c.AuthorName ?? string.Empty, Encoding.Unicode);
                    Api.SetColumn(session, commentsTableId, cColDict["AuthorHandle"], c.AuthorHandle ?? string.Empty, Encoding.Unicode);
                    Api.SetColumn(session, commentsTableId, cColDict["Content"], c.Content ?? string.Empty, Encoding.Unicode);
                    Api.SetColumn(session, commentsTableId, cColDict["TimeAgo"], c.TimeAgo ?? string.Empty, Encoding.Unicode);
                    Api.SetColumn(session, commentsTableId, cColDict["CreatedAt"], c.CreatedAt);
                    Api.SetColumn(session, commentsTableId, cColDict["LikeCount"], c.LikeCount ?? string.Empty, Encoding.Unicode);
                    Api.SetColumn(session, commentsTableId, cColDict["ReplyCount"], c.ReplyCount ?? string.Empty, Encoding.Unicode);
                    update.Save();
                }
                tx.Commit(CommitTransactionGrbit.LazyFlush);
            }
            finally
            {
                Api.JetCloseTable(session, commentsTableId);
            }
        }
        catch { }
    }

    private static void SynchronizeCommentsInternal(Session session, JET_DBID dbid, string postId, IEnumerable<PostComment>? comments)
    {
        try
        {
            Api.JetOpenTable(session, dbid, "Comments", null, 0, OpenTableGrbit.None, out JET_TABLEID commentsTableId);
            try
            {
                var cColDict = Api.GetColumnDictionary(session, commentsTableId);

                // Delete existing comments for this PostId
                if (Api.TryMoveFirst(session, commentsTableId))
                {
                    do
                    {
                        var currentPostId = Api.RetrieveColumnAsString(session, commentsTableId, cColDict["PostId"], Encoding.Unicode);
                        if (string.Equals(currentPostId, postId, StringComparison.OrdinalIgnoreCase))
                        {
                            using var txDel = new Transaction(session);
                            Api.JetDelete(session, commentsTableId);
                            txDel.Commit(CommitTransactionGrbit.LazyFlush);
                        }
                    } while (Api.TryMoveNext(session, commentsTableId));
                }

                // Insert current comments
                if (comments != null)
                {
                    using var txIns = new Transaction(session);
                    foreach (var c in comments)
                    {
                        using var update = new Update(session, commentsTableId, JET_prep.Insert);
                        Api.SetColumn(session, commentsTableId, cColDict["Id"], c.Id, Encoding.Unicode);
                        Api.SetColumn(session, commentsTableId, cColDict["PostId"], postId, Encoding.Unicode);
                        Api.SetColumn(session, commentsTableId, cColDict["AuthorName"], c.AuthorName ?? string.Empty, Encoding.Unicode);
                        Api.SetColumn(session, commentsTableId, cColDict["AuthorHandle"], c.AuthorHandle ?? string.Empty, Encoding.Unicode);
                        Api.SetColumn(session, commentsTableId, cColDict["Content"], c.Content ?? string.Empty, Encoding.Unicode);
                        Api.SetColumn(session, commentsTableId, cColDict["TimeAgo"], c.TimeAgo ?? string.Empty, Encoding.Unicode);
                        Api.SetColumn(session, commentsTableId, cColDict["CreatedAt"], c.CreatedAt);
                        Api.SetColumn(session, commentsTableId, cColDict["LikeCount"], c.LikeCount ?? string.Empty, Encoding.Unicode);
                        Api.SetColumn(session, commentsTableId, cColDict["ReplyCount"], c.ReplyCount ?? string.Empty, Encoding.Unicode);
                        update.Save();
                    }
                    txIns.Commit(CommitTransactionGrbit.LazyFlush);
                }
            }
            finally
            {
                Api.JetCloseTable(session, commentsTableId);
            }
        }
        catch { }
    }

    private static void DeleteCommentsByPostIdsInternal(Session session, JET_DBID dbid, HashSet<string> idSet)
    {
        try
        {
            Api.JetOpenTable(session, dbid, "Comments", null, 0, OpenTableGrbit.None, out JET_TABLEID commentsTableId);
            try
            {
                var cColDict = Api.GetColumnDictionary(session, commentsTableId);
                if (Api.TryMoveFirst(session, commentsTableId))
                {
                    do
                    {
                        var currentPostId = Api.RetrieveColumnAsString(session, commentsTableId, cColDict["PostId"], Encoding.Unicode);
                        if (!string.IsNullOrEmpty(currentPostId) && idSet.Contains(currentPostId))
                        {
                            using var tx = new Transaction(session);
                            Api.JetDelete(session, commentsTableId);
                            tx.Commit(CommitTransactionGrbit.LazyFlush);
                        }
                    } while (Api.TryMoveNext(session, commentsTableId));
                }
            }
            finally
            {
                Api.JetCloseTable(session, commentsTableId);
            }
        }
        catch { }
    }

    private static void InsertSingleCommentInternal(Session session, JET_DBID dbid, PostComment c)
    {
        try
        {
            Api.JetOpenTable(session, dbid, "Comments", null, 0, OpenTableGrbit.None, out JET_TABLEID commentsTableId);
            try
            {
                var cColDict = Api.GetColumnDictionary(session, commentsTableId);
                using var tx = new Transaction(session);
                using var update = new Update(session, commentsTableId, JET_prep.Insert);
                Api.SetColumn(session, commentsTableId, cColDict["Id"], c.Id, Encoding.Unicode);
                Api.SetColumn(session, commentsTableId, cColDict["PostId"], c.PostId, Encoding.Unicode);
                Api.SetColumn(session, commentsTableId, cColDict["AuthorName"], c.AuthorName ?? string.Empty, Encoding.Unicode);
                Api.SetColumn(session, commentsTableId, cColDict["AuthorHandle"], c.AuthorHandle ?? string.Empty, Encoding.Unicode);
                Api.SetColumn(session, commentsTableId, cColDict["Content"], c.Content ?? string.Empty, Encoding.Unicode);
                Api.SetColumn(session, commentsTableId, cColDict["TimeAgo"], c.TimeAgo ?? string.Empty, Encoding.Unicode);
                Api.SetColumn(session, commentsTableId, cColDict["CreatedAt"], c.CreatedAt);
                Api.SetColumn(session, commentsTableId, cColDict["LikeCount"], c.LikeCount ?? string.Empty, Encoding.Unicode);
                Api.SetColumn(session, commentsTableId, cColDict["ReplyCount"], c.ReplyCount ?? string.Empty, Encoding.Unicode);
                update.Save();
                tx.Commit(CommitTransactionGrbit.LazyFlush);
            }
            finally
            {
                Api.JetCloseTable(session, commentsTableId);
            }
        }
        catch { }
    }

    private static void CreateSampleGradientBitmap(string filePath, string label, string color1Hex, string color2Hex)
    {
        try
        {
            int width = 800;
            int height = 500;
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var color1 = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color1Hex);
                var color2 = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color2Hex);
                var brush = new System.Windows.Media.LinearGradientBrush(color1, color2, 45.0);

                dc.DrawRectangle(brush, null, new System.Windows.Rect(0, 0, width, height));

                // Draw subtle grid lines
                var pen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)), 1.0);
                for (int x = 0; x < width; x += 40)
                {
                    dc.DrawLine(pen, new System.Windows.Point(x, 0), new System.Windows.Point(x, height));
                }
                for (int y = 0; y < height; y += 40)
                {
                    dc.DrawLine(pen, new System.Windows.Point(0, y), new System.Windows.Point(width, y));
                }

                // Draw central label box
                var cardBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 15, 20, 28));
                dc.DrawRoundedRectangle(cardBrush, new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 255, 255, 255)), 1.5), new System.Windows.Rect(width / 2 - 150, height / 2 - 40, 300, 80), 12, 12);

                var formattedText = new System.Windows.Media.FormattedText(
                    $"🖼️ {label}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"), System.Windows.FontStyles.Normal, System.Windows.FontWeights.SemiBold, System.Windows.FontStretches.Normal),
                    22,
                    System.Windows.Media.Brushes.White,
                    1.0
                );

                dc.DrawText(formattedText, new System.Windows.Point((width - formattedText.Width) / 2, (height - formattedText.Height) / 2));
            }

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

            using var stream = File.Create(filePath);
            encoder.Save(stream);
        }
        catch
        {
            // Fallback
        }
    }

    public async Task<string> BackupDatabaseAsync(string targetFolderPath)
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_databasePath))
            {
                throw new FileNotFoundException("Database file does not exist to back up.", _databasePath);
            }

            Directory.CreateDirectory(targetFolderPath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupFileName = $"ArchiveData_backup_{timestamp}.edb";
            string backupFilePath = Path.Combine(targetFolderPath, backupFileName);

            await Task.Run(() =>
            {
                File.Copy(_databasePath, backupFilePath, overwrite: true);
            });

            return backupFilePath;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        bool acquired = false;
        try
        {
            acquired = _lock.Wait(TimeSpan.FromSeconds(2));
            if (_isAttached && _instance != null)
            {
                try
                {
                    using var session = new Session(_instance);
                    Api.JetDetachDatabase(session, _databasePath);
                }
                catch { }
                _isAttached = false;
            }
        }
        catch { }
        finally
        {
            if (acquired)
            {
                try { _lock.Release(); } catch { }
            }
            try { _lock.Dispose(); } catch { }
            try { _instance?.Dispose(); } catch { }
            _instance = null;
        }
    }
}

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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

    public string DatabasePath => _databasePath;

    public EseDatabaseService()
    {
        string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _baseDirectory = Path.Combine(userDocs, "LocalSocialArchive");
        _databasePath = Path.Combine(_baseDirectory, "ArchiveData.edb");
        _logDirectory = Path.Combine(_baseDirectory, "EseLogs");

        Directory.CreateDirectory(_baseDirectory);
        Directory.CreateDirectory(_logDirectory);
    }

    public async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            await Task.Run(() =>
            {
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
                _instance.Init();

                using var session = new Session(_instance);

                if (!File.Exists(_databasePath))
                {
                    // Create new database
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

                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                    Api.JetDetachDatabase(session, _databasePath);
                }
                else
                {
                    // Ensure existing DB schema has ThreadId and ThreadIndex columns
                    try
                    {
                        Api.JetAttachDatabase(session, _databasePath, AttachDatabaseGrbit.None);
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
                        }
                        finally
                        {
                            Api.JetCloseTable(session, postsTableId);
                            Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                            Api.JetDetachDatabase(session, _databasePath);
                        }
                    }
                    catch { }
                }

                _isInitialized = true;
            });
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
                Api.JetAttachDatabase(session, _databasePath, AttachDatabaseGrbit.None);
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
                }
                finally
                {
                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                    Api.JetDetachDatabase(session, _databasePath);
                }
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
                Api.JetAttachDatabase(session, _databasePath, AttachDatabaseGrbit.None);
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
                                    ThreadIndex = colDict.ContainsKey("ThreadIndex") ? (Api.RetrieveColumnAsInt32(session, postsTableId, colDict["ThreadIndex"]) ?? 0) : 0
                                };

                                if (mediaByPostId.TryGetValue(post.Id, out var mediaList))
                                {
                                    post.MediaItems = new ObservableCollection<MediaItem>(mediaList);
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
                    Api.JetDetachDatabase(session, _databasePath);
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
                Api.JetAttachDatabase(session, _databasePath, AttachDatabaseGrbit.None);
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
                                update.Save();
                            }
                            tx.Commit(CommitTransactionGrbit.LazyFlush);
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
                    Api.JetDetachDatabase(session, _databasePath);
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
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (_instance == null) throw new InvalidOperationException("ESE Database is not initialized.");

                using var session = new Session(_instance);
                Api.JetAttachDatabase(session, _databasePath, AttachDatabaseGrbit.None);
                Api.JetOpenDatabase(session, _databasePath, null, out JET_DBID dbid, OpenDatabaseGrbit.None);

                try
                {
                    // 1. Delete Post
                    Api.JetOpenTable(session, dbid, "Posts", null, 0, OpenTableGrbit.None, out JET_TABLEID postsTableId);
                    try
                    {
                        Api.JetSetCurrentIndex(session, postsTableId, "PK_Posts");
                        Api.MakeKey(session, postsTableId, postId, Encoding.Unicode, MakeKeyGrbit.NewKey);
                        if (Api.TrySeek(session, postsTableId, SeekGrbit.SeekEQ))
                        {
                            using var tx = new Transaction(session);
                            Api.JetDelete(session, postsTableId);
                            tx.Commit(CommitTransactionGrbit.LazyFlush);
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
                                if (string.Equals(currentPostId, postId, StringComparison.OrdinalIgnoreCase))
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
                }
                finally
                {
                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                    Api.JetDetachDatabase(session, _databasePath);
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

        await InsertPostAsync(samplePost1);
        await InsertPostAsync(samplePost2);
        await InsertPostAsync(samplePost3);
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

    public void Dispose()
    {
        _lock.Dispose();
        _instance?.Dispose();
        _instance = null;
    }
}

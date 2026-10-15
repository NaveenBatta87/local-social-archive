using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Isam.Esent.Interop;

namespace LocalSocialArchive.Services;

/// <summary>
/// High-performance SQLite database service for Local Social Archive.
/// Features Write-Ahead Logging (WAL) concurrency, foreign key cascades,
/// FTS5 full-text search, and automatic zero-data-loss migration from legacy ESE databases.
/// </summary>
public class SqliteDatabaseService : IEseDatabaseService, IDisposable
{
    private readonly string _databasePath;
    private readonly string _baseDirectory;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private SqliteConnection? _connection;
    private bool _isInitialized;

    public string DatabasePath => _databasePath;

    public SqliteDatabaseService()
    {
        _baseDirectory = StoragePathHelper.BaseDirectory;
        _databasePath = StoragePathHelper.SqliteDatabasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        Directory.CreateDirectory(_baseDirectory);
        Directory.CreateDirectory(StoragePathHelper.MediaDirectory);
    }

    public async Task InitializeAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            StoragePathHelper.LogDiagnostic($"[SQLite] Initializing database at '{_databasePath}'...");

            _connection = new SqliteConnection(_connectionString);
            await _connection.OpenAsync();

            // 1. Configure high-performance pragmas
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA foreign_keys = ON;
                    PRAGMA busy_timeout = 5000;
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // 2. Create Relational Tables & Indexes
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Posts (
                        Id TEXT PRIMARY KEY NOT NULL,
                        AuthorName TEXT NOT NULL DEFAULT 'Offline User',
                        AuthorHandle TEXT NOT NULL DEFAULT '@offline',
                        Content TEXT NOT NULL DEFAULT '',
                        PostDate TEXT NOT NULL,
                        FolderName TEXT NOT NULL DEFAULT 'Coding' COLLATE NOCASE,
                        Tags TEXT NOT NULL DEFAULT '',
                        Comment TEXT,
                        FolderPath TEXT NOT NULL DEFAULT '',
                        CreatedAt TEXT NOT NULL,
                        ThreadId TEXT,
                        ThreadIndex INTEGER NOT NULL DEFAULT 0
                    );

                    CREATE INDEX IF NOT EXISTS IX_Posts_FolderName ON Posts(FolderName);
                    CREATE INDEX IF NOT EXISTS IX_Posts_ThreadId ON Posts(ThreadId, ThreadIndex);
                    CREATE INDEX IF NOT EXISTS IX_Posts_PostDate ON Posts(PostDate DESC);
                    CREATE INDEX IF NOT EXISTS IX_Posts_CreatedAt ON Posts(CreatedAt DESC);

                    CREATE TABLE IF NOT EXISTS Media (
                        Id TEXT PRIMARY KEY NOT NULL,
                        PostId TEXT NOT NULL,
                        FilePath TEXT NOT NULL,
                        MediaType TEXT NOT NULL DEFAULT 'Image',
                        ThumbnailPath TEXT,
                        CreatedAt TEXT NOT NULL,
                        FOREIGN KEY (PostId) REFERENCES Posts(Id) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS IX_Media_PostId ON Media(PostId);

                    CREATE TABLE IF NOT EXISTS Comments (
                        Id TEXT PRIMARY KEY NOT NULL,
                        PostId TEXT NOT NULL,
                        AuthorName TEXT NOT NULL DEFAULT '',
                        AuthorHandle TEXT NOT NULL DEFAULT '',
                        Content TEXT NOT NULL DEFAULT '',
                        TimeAgo TEXT NOT NULL DEFAULT '',
                        CreatedAt TEXT NOT NULL,
                        LikeCount TEXT NOT NULL DEFAULT '',
                        ReplyCount TEXT NOT NULL DEFAULT '',
                        IsPinned INTEGER NOT NULL DEFAULT 0,
                        IsHearted INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (PostId) REFERENCES Posts(Id) ON DELETE CASCADE
                    );

                    CREATE INDEX IF NOT EXISTS IX_Comments_PostId ON Comments(PostId);
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // 3. Create FTS5 Full-Text Search Virtual Table & Triggers
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE VIRTUAL TABLE IF NOT EXISTS Posts_Fts USING fts5(
                        Id UNINDEXED,
                        AuthorName,
                        AuthorHandle,
                        Content,
                        Tags,
                        FolderName,
                        Comment,
                        tokenize = 'unicode61 remove_diacritics 2'
                    );

                    CREATE TRIGGER IF NOT EXISTS TR_Posts_Fts_Insert AFTER INSERT ON Posts BEGIN
                        INSERT INTO Posts_Fts(Id, AuthorName, AuthorHandle, Content, Tags, FolderName, Comment)
                        VALUES (new.Id, new.AuthorName, new.AuthorHandle, new.Content, new.Tags, new.FolderName, coalesce(new.Comment, ''));
                    END;

                    CREATE TRIGGER IF NOT EXISTS TR_Posts_Fts_Update AFTER UPDATE ON Posts BEGIN
                        DELETE FROM Posts_Fts WHERE Id = old.Id;
                        INSERT INTO Posts_Fts(Id, AuthorName, AuthorHandle, Content, Tags, FolderName, Comment)
                        VALUES (new.Id, new.AuthorName, new.AuthorHandle, new.Content, new.Tags, new.FolderName, coalesce(new.Comment, ''));
                    END;

                    CREATE TRIGGER IF NOT EXISTS TR_Posts_Fts_Delete AFTER DELETE ON Posts BEGIN
                        DELETE FROM Posts_Fts WHERE Id = old.Id;
                    END;
                ";
                await cmd.ExecuteNonQueryAsync();
                StoragePathHelper.LogDiagnostic("[SQLite] FTS5 full-text search established successfully.");
            }
            catch (Exception ex)
            {
                StoragePathHelper.LogDiagnostic($"[SQLite FTS5 Warning] {ex.Message}. Standard search fallback will be used.");
            }

            // 4. Check if SQLite database is empty
            long postCount = 0;
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Posts;";
                var result = await cmd.ExecuteScalarAsync();
                postCount = result is long l ? l : Convert.ToInt64(result);
            }

            // 4. Check existing posts
            HashSet<string> existingIds = new(StringComparer.OrdinalIgnoreCase);
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM Posts;";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingIds.Add(reader.GetString(0));
                }
            }

            // 5. Zero-Data-Loss Migration Check:
            string localEseDb = StoragePathHelper.LegacyEseDatabasePath;
            string docsEseDb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LocalSocialArchive", "ArchiveData.edb");
            string? eseSourceToMigrate = File.Exists(localEseDb) ? localEseDb : (File.Exists(docsEseDb) ? docsEseDb : null);

            if (eseSourceToMigrate != null && existingIds.Count == 0)
            {
                StoragePathHelper.LogDiagnostic($"[SQLite Migration] Detected legacy ESE database at '{eseSourceToMigrate}'. Starting automated migration...");
                try
                {
                    await MigrateFromLegacyEseAsync(eseSourceToMigrate);
                }
                catch (Exception ex)
                {
                    StoragePathHelper.LogDiagnostic($"[SQLite Migration Error] Failed to migrate from ESE: {ex.Message}");
                }
            }

            // 6. Ensure any posts from Media directories that are missing from SQLite get restored
            var mediaRecovered = RecoverPostsFromMediaDirectory(StoragePathHelper.MediaDirectory, existingIds);
            string legacyMedia = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LocalSocialArchive", "Media");
            if (Directory.Exists(legacyMedia))
            {
                var legacyMediaRecovered = RecoverPostsFromMediaDirectory(legacyMedia, existingIds);
                foreach (var p in legacyMediaRecovered)
                {
                    if (existingIds.Add(p.Id))
                    {
                        mediaRecovered.Add(p);
                    }
                }
            }

            if (mediaRecovered.Count > 0)
            {
                StoragePathHelper.LogDiagnostic($"[SQLite Recovery] Auto-syncing {mediaRecovered.Count} archive posts from Media storage...");
                await InsertPostsBatchInternalAsync(mediaRecovered);
                StoragePathHelper.LogDiagnostic($"[SQLite Recovery] Successfully synced {mediaRecovered.Count} posts into SQLite.");
            }

            _isInitialized = true;
            StoragePathHelper.LogDiagnostic("[SQLite] Database initialization completed.");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Reads all records from a legacy ESE database and migrates them atomically to SQLite.
    /// Renames the ESE file to .backup so no user data is ever destroyed.
    /// </summary>
    private async Task MigrateFromLegacyEseAsync(string esePath)
    {
        string eseDir = Path.GetDirectoryName(esePath)!;
        string eseLogs = Path.Combine(eseDir, "EseLogs");
        Directory.CreateDirectory(eseLogs);

        // Ensure transaction logs from legacy directory are available for ESE soft recovery
        try
        {
            string legacyDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string legacyEseLogs = Path.Combine(legacyDocs, "LocalSocialArchive", "EseLogs");
            if (Directory.Exists(legacyEseLogs))
            {
                foreach (var logFile in Directory.GetFiles(legacyEseLogs))
                {
                    string dest = Path.Combine(eseLogs, Path.GetFileName(logFile));
                    if (!File.Exists(dest))
                    {
                        File.Copy(logFile, dest, overwrite: false);
                    }
                }
            }
        }
        catch { }

        List<Post> migratedPosts = new();

        await Task.Run(() =>
        {
            try
            {
                SystemParameters.DatabasePageSize = 8192;
                using var eseInstance = new Instance($"MigrateEse_{DateTime.Now.Ticks}");
                eseInstance.Parameters.CreatePathIfNotExist = true;
                eseInstance.Parameters.CircularLog = true;
                eseInstance.Parameters.LogFileSize = 512;
                eseInstance.Parameters.MaxVerPages = 1024;
                eseInstance.Parameters.SystemDirectory = eseLogs;
                eseInstance.Parameters.LogFileDirectory = eseLogs;
                eseInstance.Parameters.TempDirectory = eseLogs;
                eseInstance.Parameters.Recovery = true;
                eseInstance.Parameters.BaseName = "edb";
                eseInstance.Init();

                using var session = new Session(eseInstance);
                Api.JetAttachDatabase(session, esePath, AttachDatabaseGrbit.ReadOnly);
                Api.JetOpenDatabase(session, esePath, null, out JET_DBID dbid, OpenDatabaseGrbit.ReadOnly);

                try
                {
                    // 1. Read Media
                    var mediaByPostId = new Dictionary<string, List<MediaItem>>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        Api.JetOpenTable(session, dbid, "Media", null, 0, OpenTableGrbit.ReadOnly, out JET_TABLEID mediaTable);
                        try
                        {
                            var mCols = Api.GetColumnDictionary(session, mediaTable);
                            if (Api.TryMoveFirst(session, mediaTable))
                            {
                                do
                                {
                                    var item = new MediaItem
                                    {
                                        Id = Api.RetrieveColumnAsString(session, mediaTable, mCols["Id"], Encoding.Unicode) ?? Guid.NewGuid().ToString("N"),
                                        PostId = Api.RetrieveColumnAsString(session, mediaTable, mCols["PostId"], Encoding.Unicode) ?? string.Empty,
                                        FilePath = Api.RetrieveColumnAsString(session, mediaTable, mCols["FilePath"], Encoding.Unicode) ?? string.Empty,
                                        MediaType = Api.RetrieveColumnAsString(session, mediaTable, mCols["MediaType"], Encoding.Unicode) ?? "Image",
                                        ThumbnailPath = Api.RetrieveColumnAsString(session, mediaTable, mCols["ThumbnailPath"], Encoding.Unicode),
                                        CreatedAt = Api.RetrieveColumnAsDateTime(session, mediaTable, mCols["CreatedAt"]) ?? DateTime.Now
                                    };
                                    if (!mediaByPostId.TryGetValue(item.PostId, out var mList))
                                    {
                                        mList = new List<MediaItem>();
                                        mediaByPostId[item.PostId] = mList;
                                    }
                                    mList.Add(item);
                                } while (Api.TryMoveNext(session, mediaTable));
                            }
                        }
                        finally
                        {
                            Api.JetCloseTable(session, mediaTable);
                        }
                    }
                    catch { }

                    // 2. Read Comments
                    var commentsByPostId = new Dictionary<string, List<PostComment>>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        Api.JetOpenTable(session, dbid, "Comments", null, 0, OpenTableGrbit.ReadOnly, out JET_TABLEID commTable);
                        try
                        {
                            var cCols = Api.GetColumnDictionary(session, commTable);
                            if (Api.TryMoveFirst(session, commTable))
                            {
                                do
                                {
                                    var c = new PostComment
                                    {
                                        Id = Api.RetrieveColumnAsString(session, commTable, cCols["Id"], Encoding.Unicode) ?? Guid.NewGuid().ToString("N"),
                                        PostId = Api.RetrieveColumnAsString(session, commTable, cCols["PostId"], Encoding.Unicode) ?? string.Empty,
                                        AuthorName = Api.RetrieveColumnAsString(session, commTable, cCols["AuthorName"], Encoding.Unicode) ?? string.Empty,
                                        AuthorHandle = Api.RetrieveColumnAsString(session, commTable, cCols["AuthorHandle"], Encoding.Unicode) ?? string.Empty,
                                        Content = Api.RetrieveColumnAsString(session, commTable, cCols["Content"], Encoding.Unicode) ?? string.Empty,
                                        TimeAgo = cCols.ContainsKey("TimeAgo") ? (Api.RetrieveColumnAsString(session, commTable, cCols["TimeAgo"], Encoding.Unicode) ?? string.Empty) : string.Empty,
                                        CreatedAt = Api.RetrieveColumnAsDateTime(session, commTable, cCols["CreatedAt"]) ?? DateTime.Now,
                                        LikeCount = cCols.ContainsKey("LikeCount") ? (Api.RetrieveColumnAsString(session, commTable, cCols["LikeCount"], Encoding.Unicode) ?? string.Empty) : string.Empty,
                                        ReplyCount = cCols.ContainsKey("ReplyCount") ? (Api.RetrieveColumnAsString(session, commTable, cCols["ReplyCount"], Encoding.Unicode) ?? string.Empty) : string.Empty
                                    };
                                    if (!commentsByPostId.TryGetValue(c.PostId, out var cList))
                                    {
                                        cList = new List<PostComment>();
                                        commentsByPostId[c.PostId] = cList;
                                    }
                                    cList.Add(c);
                                } while (Api.TryMoveNext(session, commTable));
                            }
                        }
                        finally
                        {
                            Api.JetCloseTable(session, commTable);
                        }
                    }
                    catch { }

                    // 3. Read Posts
                    Api.JetOpenTable(session, dbid, "Posts", null, 0, OpenTableGrbit.ReadOnly, out JET_TABLEID postsTable);
                    try
                    {
                        var pCols = Api.GetColumnDictionary(session, postsTable);
                        if (Api.TryMoveFirst(session, postsTable))
                        {
                            do
                            {
                                string pid = Api.RetrieveColumnAsString(session, postsTable, pCols["Id"], Encoding.Unicode) ?? Guid.NewGuid().ToString("N");
                                var post = new Post
                                {
                                    Id = pid,
                                    AuthorName = Api.RetrieveColumnAsString(session, postsTable, pCols["AuthorName"], Encoding.Unicode) ?? "Offline User",
                                    AuthorHandle = Api.RetrieveColumnAsString(session, postsTable, pCols["AuthorHandle"], Encoding.Unicode) ?? "@offline",
                                    Content = Api.RetrieveColumnAsString(session, postsTable, pCols["Content"], Encoding.Unicode) ?? string.Empty,
                                    PostDate = Api.RetrieveColumnAsDateTime(session, postsTable, pCols["PostDate"]) ?? DateTime.Now,
                                    FolderName = Api.RetrieveColumnAsString(session, postsTable, pCols["FolderName"], Encoding.Unicode) ?? "Coding",
                                    Tags = Api.RetrieveColumnAsString(session, postsTable, pCols["Tags"], Encoding.Unicode) ?? string.Empty,
                                    FolderPath = Api.RetrieveColumnAsString(session, postsTable, pCols["FolderPath"], Encoding.Unicode) ?? string.Empty,
                                    CreatedAt = Api.RetrieveColumnAsDateTime(session, postsTable, pCols["CreatedAt"]) ?? DateTime.Now,
                                    ThreadId = pCols.ContainsKey("ThreadId") ? Api.RetrieveColumnAsString(session, postsTable, pCols["ThreadId"], Encoding.Unicode) : null,
                                    ThreadIndex = pCols.ContainsKey("ThreadIndex") ? (Api.RetrieveColumnAsInt32(session, postsTable, pCols["ThreadIndex"]) ?? 0) : 0,
                                    Comment = pCols.ContainsKey("Comment") ? Api.RetrieveColumnAsString(session, postsTable, pCols["Comment"], Encoding.Unicode) : null
                                };

                                if (mediaByPostId.TryGetValue(pid, out var mediaItems))
                                {
                                    post.MediaItems = new ObservableCollection<MediaItem>(mediaItems);
                                }
                                if (commentsByPostId.TryGetValue(pid, out var comments))
                                {
                                    post.Comments = new ObservableCollection<PostComment>(comments);
                                }

                                migratedPosts.Add(post);
                            } while (Api.TryMoveNext(session, postsTable));
                        }
                    }
                    finally
                    {
                        Api.JetCloseTable(session, postsTable);
                    }

                    Api.JetCloseDatabase(session, dbid, CloseDatabaseGrbit.None);
                    Api.JetDetachDatabase(session, esePath);
                }
                catch (Exception ex)
                {
                    StoragePathHelper.LogDiagnostic($"[ESE Reader Warning] {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                StoragePathHelper.LogDiagnostic($"[ESE Extraction Error] {ex.Message}");
            }
        });

        // Fallback: If direct ESE table read yielded 0 posts (e.g. dirty shutdown state),
        // recover directly from post.json metadata files preserved in Media directories
        if (migratedPosts.Count == 0)
        {
            StoragePathHelper.LogDiagnostic("[SQLite Migration] ESE table read yielded 0 posts. Running automatic recovery from Media post.json archives...");
            var mediaRecovered = RecoverPostsFromMediaDirectory(StoragePathHelper.MediaDirectory);
            if (mediaRecovered.Count == 0)
            {
                string legacyMedia = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LocalSocialArchive", "Media");
                mediaRecovered = RecoverPostsFromMediaDirectory(legacyMedia);
            }

            if (mediaRecovered.Count > 0)
            {
                migratedPosts.AddRange(mediaRecovered);
                StoragePathHelper.LogDiagnostic($"[SQLite Migration] Recovered {mediaRecovered.Count} posts with full fidelity from Media JSON archives.");
            }
        }

        if (migratedPosts.Count > 0)
        {
            StoragePathHelper.LogDiagnostic($"[SQLite Migration] Inserting {migratedPosts.Count} posts into SQLite...");
            await InsertPostsBatchInternalAsync(migratedPosts);

            // Rename ESE database to .backup
            try
            {
                string backupEsePath = esePath + ".backup";
                if (!File.Exists(backupEsePath))
                {
                    File.Move(esePath, backupEsePath);
                    StoragePathHelper.LogDiagnostic($"[SQLite Migration] Renamed '{esePath}' to '{backupEsePath}'. Raw backup preserved.");
                }

                // Clean up old EseLogs
                if (Directory.Exists(eseLogs))
                {
                    foreach (var f in Directory.GetFiles(eseLogs))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                StoragePathHelper.LogDiagnostic($"[SQLite Backup Notice] {ex.Message}");
            }

            StoragePathHelper.LogDiagnostic($"[SQLite Migration] Successfully migrated {migratedPosts.Count} posts with all media and comments to SQLite!");
        }
    }

    private List<Post> RecoverPostsFromMediaDirectory(string mediaDir, HashSet<string>? existingPostIds = null)
    {
        var recovered = new List<Post>();
        if (!Directory.Exists(mediaDir)) return recovered;

        foreach (var subDir in Directory.GetDirectories(mediaDir))
        {
            string jsonPath = Path.Combine(subDir, "post.json");
            if (!File.Exists(jsonPath)) continue;

            try
            {
                // Trim any UTF-8 Byte Order Mark (BOM) or whitespace before parsing JSON
                string jsonText = File.ReadAllText(jsonPath).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
                using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                string id = root.TryGetProperty("id", out var pId) ? (pId.GetString() ?? Guid.NewGuid().ToString("N")) : Guid.NewGuid().ToString("N");
                if (existingPostIds != null && existingPostIds.Contains(id))
                {
                    continue; // Already exists in SQLite
                }

                string authorName = root.TryGetProperty("authorName", out var pName) ? (pName.GetString() ?? "Offline User") : "Offline User";
                string authorHandle = root.TryGetProperty("authorHandle", out var pHandle) ? (pHandle.GetString() ?? "@offline") : "@offline";
                string content = root.TryGetProperty("content", out var pContent) ? (pContent.GetString() ?? string.Empty) : string.Empty;
                string? comment = root.TryGetProperty("comment", out var pComment) && pComment.ValueKind == System.Text.Json.JsonValueKind.String ? pComment.GetString() : null;
                string folderName = root.TryGetProperty("folderName", out var pFolder) ? (pFolder.GetString() ?? "Coding") : "Coding";

                DateTime postDate = DateTime.Now;
                if (root.TryGetProperty("postDate", out var pDate) && pDate.TryGetDateTime(out var dt))
                {
                    postDate = dt;
                }

                string mdPath = Path.Combine(subDir, "post.md");
                if (File.Exists(mdPath) && (string.IsNullOrWhiteSpace(content) || content.StartsWith("Version:")))
                {
                    try
                    {
                        string mdContent = File.ReadAllText(mdPath);
                        if (!string.IsNullOrWhiteSpace(mdContent))
                        {
                            content = mdContent;
                        }
                    }
                    catch { }
                }

                var tagsList = new List<string>();
                if (root.TryGetProperty("tags", out var pTags) && pTags.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var tagElem in pTags.EnumerateArray())
                    {
                        var t = tagElem.GetString();
                        if (!string.IsNullOrWhiteSpace(t)) tagsList.Add(t);
                    }
                }

                var post = new Post
                {
                    Id = id,
                    AuthorName = authorName,
                    AuthorHandle = authorHandle,
                    Content = content,
                    Comment = comment,
                    FolderName = folderName,
                    PostDate = postDate,
                    CreatedAt = postDate,
                    Tags = string.Join(",", tagsList),
                    FolderPath = subDir
                };

                if (root.TryGetProperty("mediaFiles", out var pMedia) && pMedia.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var mElem in pMedia.EnumerateArray())
                    {
                        string mName = mElem.TryGetProperty("name", out var pn) ? (pn.GetString() ?? string.Empty) : string.Empty;
                        string mType = mElem.TryGetProperty("type", out var pt) ? (pt.GetString() ?? "Image") : "Image";
                        string localFilePath = Path.Combine(subDir, mName);
                        if (!File.Exists(localFilePath) && mElem.TryGetProperty("path", out var pp))
                        {
                            localFilePath = pp.GetString() ?? localFilePath;
                        }

                        if (File.Exists(localFilePath))
                        {
                            post.MediaItems.Add(new MediaItem
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                PostId = id,
                                FilePath = localFilePath,
                                MediaType = mType,
                                CreatedAt = postDate
                            });
                        }
                    }
                }
                else
                {
                    foreach (var mediaFile in Directory.GetFiles(subDir))
                    {
                        string ext = Path.GetExtension(mediaFile).ToLowerInvariant();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".mp4" or ".mov" or ".webm")
                        {
                            post.MediaItems.Add(new MediaItem
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                PostId = id,
                                FilePath = mediaFile,
                                MediaType = (ext is ".mp4" or ".mov" or ".webm") ? "Video" : "Image",
                                CreatedAt = postDate
                            });
                        }
                    }
                }

                recovered.Add(post);
            }
            catch (Exception ex)
            {
                StoragePathHelper.LogDiagnostic($"[Media Post Recovery Error in {subDir}] {ex.Message}");
            }
        }

        return recovered;
    }

    public async Task<List<Post>> GetAllPostsAsync()
    {
        EnsureInitialized();
        var posts = new List<Post>();
        var mediaByPostId = new Dictionary<string, List<MediaItem>>(StringComparer.OrdinalIgnoreCase);
        var commentsByPostId = new Dictionary<string, List<PostComment>>(StringComparer.OrdinalIgnoreCase);

        // 1. Fetch Media
        using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, PostId, FilePath, MediaType, ThumbnailPath, CreatedAt FROM Media;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var m = new MediaItem
                {
                    Id = reader.GetString(0),
                    PostId = reader.GetString(1),
                    FilePath = reader.GetString(2),
                    MediaType = reader.GetString(3),
                    ThumbnailPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                    CreatedAt = ParseDateTime(reader.GetString(5))
                };
                if (!mediaByPostId.TryGetValue(m.PostId, out var list))
                {
                    list = new List<MediaItem>();
                    mediaByPostId[m.PostId] = list;
                }
                list.Add(m);
            }
        }

        // 2. Fetch Comments
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, PostId, AuthorName, AuthorHandle, Content, TimeAgo, CreatedAt, LikeCount, ReplyCount, IsPinned, IsHearted FROM Comments;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var c = new PostComment
                {
                    Id = reader.GetString(0),
                    PostId = reader.GetString(1),
                    AuthorName = reader.GetString(2),
                    AuthorHandle = reader.GetString(3),
                    Content = reader.GetString(4),
                    TimeAgo = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    CreatedAt = ParseDateTime(reader.GetString(6)),
                    LikeCount = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    ReplyCount = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    IsPinned = reader.GetInt32(9) == 1,
                    IsHearted = reader.GetInt32(10) == 1
                };
                if (!commentsByPostId.TryGetValue(c.PostId, out var list))
                {
                    list = new List<PostComment>();
                    commentsByPostId[c.PostId] = list;
                }
                list.Add(c);
            }
        }

        // 3. Fetch Posts
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, AuthorName, AuthorHandle, Content, PostDate, FolderName, Tags, Comment, FolderPath, CreatedAt, ThreadId, ThreadIndex 
                FROM Posts 
                ORDER BY PostDate DESC;
            ";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string id = reader.GetString(0);
                var post = new Post
                {
                    Id = id,
                    AuthorName = reader.GetString(1),
                    AuthorHandle = reader.GetString(2),
                    Content = reader.GetString(3),
                    PostDate = ParseDateTime(reader.GetString(4)),
                    FolderName = reader.GetString(5),
                    Tags = reader.GetString(6),
                    Comment = reader.IsDBNull(7) ? null : reader.GetString(7),
                    FolderPath = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    CreatedAt = ParseDateTime(reader.GetString(9)),
                    ThreadId = reader.IsDBNull(10) ? null : reader.GetString(10),
                    ThreadIndex = reader.GetInt32(11)
                };

                if (mediaByPostId.TryGetValue(id, out var mediaList))
                {
                    post.MediaItems = new ObservableCollection<MediaItem>(mediaList);
                }
                if (commentsByPostId.TryGetValue(id, out var commentList))
                {
                    post.Comments = new ObservableCollection<PostComment>(commentList);
                }

                posts.Add(post);
            }
        }

        return posts;
    }

    public async Task<List<Post>> GetPostsByFolderAsync(string folderName)
    {
        var all = await GetAllPostsAsync();
        return all.Where(p => string.Equals(p.FolderName, folderName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<List<Post>> GetPostsByTagAsync(string tagName)
    {
        var all = await GetAllPostsAsync();
        return all.Where(p => p.Tags.IndexOf(tagName, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
    }

    public async Task<List<Post>> SearchPostsAsync(string query)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(query)) return await GetAllPostsAsync();

        // 1. Try FTS5 fast ranked search
        try
        {
            string ftsTerm = query.Trim().Replace("'", "''").Replace("\"", "");
            // Support prefix matching on words
            var tokens = ftsTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string formattedQuery = string.Join(" ", tokens.Select(t => $"{t}*"));

            var matchingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM Posts_Fts WHERE Posts_Fts MATCH @Query ORDER BY rank LIMIT 500;";
                cmd.Parameters.AddWithValue("@Query", formattedQuery);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    matchingIds.Add(reader.GetString(0));
                }
            }

            if (matchingIds.Count > 0)
            {
                var all = await GetAllPostsAsync();
                return all.Where(p => matchingIds.Contains(p.Id)).ToList();
            }
        }
        catch (Exception ex)
        {
            StoragePathHelper.LogDiagnostic($"[SQLite FTS5 Fallback] {ex.Message}");
        }

        // 2. Fallback to in-memory search
        var fallbackList = await GetAllPostsAsync();
        string cleanQ = query.Trim().ToLowerInvariant();
        return fallbackList.Where(p =>
            p.Content.ToLowerInvariant().Contains(cleanQ) ||
            p.AuthorName.ToLowerInvariant().Contains(cleanQ) ||
            p.AuthorHandle.ToLowerInvariant().Contains(cleanQ) ||
            p.Tags.ToLowerInvariant().Contains(cleanQ) ||
            p.FolderName.ToLowerInvariant().Contains(cleanQ) ||
            (p.Comment != null && p.Comment.ToLowerInvariant().Contains(cleanQ))
        ).ToList();
    }

    public async Task<Post?> GetPostByIdAsync(string postId)
    {
        var all = await GetAllPostsAsync();
        return all.FirstOrDefault(p => p.Id == postId);
    }

    public async Task InsertPostAsync(Post post)
    {
        EnsureInitialized();
        await _writeLock.WaitAsync();
        try
        {
            using var tx = _connection!.BeginTransaction();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO Posts (Id, AuthorName, AuthorHandle, Content, PostDate, FolderName, Tags, Comment, FolderPath, CreatedAt, ThreadId, ThreadIndex)
                    VALUES (@Id, @AuthorName, @AuthorHandle, @Content, @PostDate, @FolderName, @Tags, @Comment, @FolderPath, @CreatedAt, @ThreadId, @ThreadIndex);
                ";
                BindPostParameters(cmd, post);
                await cmd.ExecuteNonQueryAsync();
            }

            if (post.MediaItems != null && post.MediaItems.Count > 0)
            {
                foreach (var media in post.MediaItems)
                {
                    using var mCmd = _connection.CreateCommand();
                    mCmd.Transaction = tx;
                    mCmd.CommandText = @"
                        INSERT OR REPLACE INTO Media (Id, PostId, FilePath, MediaType, ThumbnailPath, CreatedAt)
                        VALUES (@Id, @PostId, @FilePath, @MediaType, @ThumbnailPath, @CreatedAt);
                    ";
                    mCmd.Parameters.AddWithValue("@Id", media.Id);
                    mCmd.Parameters.AddWithValue("@PostId", post.Id);
                    mCmd.Parameters.AddWithValue("@FilePath", media.FilePath);
                    mCmd.Parameters.AddWithValue("@MediaType", media.MediaType ?? "Image");
                    mCmd.Parameters.AddWithValue("@ThumbnailPath", (object?)media.ThumbnailPath ?? DBNull.Value);
                    mCmd.Parameters.AddWithValue("@CreatedAt", FormatDateTime(media.CreatedAt));
                    await mCmd.ExecuteNonQueryAsync();
                }
            }

            if (post.Comments != null && post.Comments.Count > 0)
            {
                foreach (var comm in post.Comments)
                {
                    using var cCmd = _connection.CreateCommand();
                    cCmd.Transaction = tx;
                    cCmd.CommandText = @"
                        INSERT OR REPLACE INTO Comments (Id, PostId, AuthorName, AuthorHandle, Content, TimeAgo, CreatedAt, LikeCount, ReplyCount, IsPinned, IsHearted)
                        VALUES (@Id, @PostId, @AuthorName, @AuthorHandle, @Content, @TimeAgo, @CreatedAt, @LikeCount, @ReplyCount, @IsPinned, @IsHearted);
                    ";
                    BindCommentParameters(cCmd, post.Id, comm);
                    await cCmd.ExecuteNonQueryAsync();
                }
            }

            await tx.CommitAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<int> InsertPostsBatchAsync(IEnumerable<Post> posts, IProgress<(int current, int total, string status)>? progress = null)
    {
        var list = posts.Where(p => p != null).ToList();
        if (list.Count == 0) return 0;

        EnsureInitialized();
        await _writeLock.WaitAsync();
        try
        {
            return await InsertPostsBatchInternalAsync(list, progress);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<int> InsertPostsBatchInternalAsync(List<Post> list, IProgress<(int current, int total, string status)>? progress = null)
    {
        int inserted = 0;
        int total = list.Count;
        const int chunkSize = 500;

        for (int i = 0; i < total; i += chunkSize)
        {
            var chunk = list.Skip(i).Take(chunkSize).ToList();
            using var tx = _connection!.BeginTransaction();

            foreach (var post in chunk)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO Posts (Id, AuthorName, AuthorHandle, Content, PostDate, FolderName, Tags, Comment, FolderPath, CreatedAt, ThreadId, ThreadIndex)
                        VALUES (@Id, @AuthorName, @AuthorHandle, @Content, @PostDate, @FolderName, @Tags, @Comment, @FolderPath, @CreatedAt, @ThreadId, @ThreadIndex);
                    ";
                    BindPostParameters(cmd, post);
                    await cmd.ExecuteNonQueryAsync();
                }

                if (post.MediaItems != null && post.MediaItems.Count > 0)
                {
                    foreach (var media in post.MediaItems)
                    {
                        using var mCmd = _connection.CreateCommand();
                        mCmd.Transaction = tx;
                        mCmd.CommandText = @"
                            INSERT OR REPLACE INTO Media (Id, PostId, FilePath, MediaType, ThumbnailPath, CreatedAt)
                            VALUES (@Id, @PostId, @FilePath, @MediaType, @ThumbnailPath, @CreatedAt);
                        ";
                        mCmd.Parameters.AddWithValue("@Id", media.Id);
                        mCmd.Parameters.AddWithValue("@PostId", post.Id);
                        mCmd.Parameters.AddWithValue("@FilePath", media.FilePath);
                        mCmd.Parameters.AddWithValue("@MediaType", media.MediaType ?? "Image");
                        mCmd.Parameters.AddWithValue("@ThumbnailPath", (object?)media.ThumbnailPath ?? DBNull.Value);
                        mCmd.Parameters.AddWithValue("@CreatedAt", FormatDateTime(media.CreatedAt));
                        await mCmd.ExecuteNonQueryAsync();
                    }
                }

                if (post.Comments != null && post.Comments.Count > 0)
                {
                    foreach (var comm in post.Comments)
                    {
                        using var cCmd = _connection.CreateCommand();
                        cCmd.Transaction = tx;
                        cCmd.CommandText = @"
                            INSERT OR REPLACE INTO Comments (Id, PostId, AuthorName, AuthorHandle, Content, TimeAgo, CreatedAt, LikeCount, ReplyCount, IsPinned, IsHearted)
                            VALUES (@Id, @PostId, @AuthorName, @AuthorHandle, @Content, @TimeAgo, @CreatedAt, @LikeCount, @ReplyCount, @IsPinned, @IsHearted);
                        ";
                        BindCommentParameters(cCmd, post.Id, comm);
                        await cCmd.ExecuteNonQueryAsync();
                    }
                }

                inserted++;
            }

            await tx.CommitAsync();
            progress?.Report((inserted, total, $"Imported {inserted} of {total} posts..."));
        }

        return inserted;
    }

    public async Task UpdatePostAsync(Post post)
    {
        await InsertPostAsync(post);
    }

    public async Task DeletePostAsync(string postId)
    {
        await DeletePostsAsync(new[] { postId });
    }

    public async Task DeletePostsAsync(IEnumerable<string> postIds)
    {
        var idList = postIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (idList.Count == 0) return;

        EnsureInitialized();
        await _writeLock.WaitAsync();
        try
        {
            // 1. Collect media files to delete from disk
            var filesToDelete = new List<string>();
            foreach (var pid in idList)
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT FilePath, ThumbnailPath FROM Media WHERE PostId = @PostId;";
                cmd.Parameters.AddWithValue("@PostId", pid);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0)) filesToDelete.Add(reader.GetString(0));
                    if (!reader.IsDBNull(1)) filesToDelete.Add(reader.GetString(1));
                }
            }

            // 2. Delete posts from SQLite (Foreign keys cascade to Media and Comments automatically)
            using var tx = _connection!.BeginTransaction();
            foreach (var pid in idList)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM Posts WHERE Id = @Id;";
                cmd.Parameters.AddWithValue("@Id", pid);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();

            // 3. Clean physical files from disk
            _ = Task.Run(() =>
            {
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        if (File.Exists(file)) File.Delete(file);
                    }
                    catch { }
                }
            });
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SaveCommentsForPostAsync(string postId, IEnumerable<PostComment> comments)
    {
        EnsureInitialized();
        await _writeLock.WaitAsync();
        try
        {
            using var tx = _connection!.BeginTransaction();

            using (var delCmd = _connection.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = "DELETE FROM Comments WHERE PostId = @PostId;";
                delCmd.Parameters.AddWithValue("@PostId", postId);
                await delCmd.ExecuteNonQueryAsync();
            }

            if (comments != null)
            {
                foreach (var c in comments)
                {
                    using var insCmd = _connection.CreateCommand();
                    insCmd.Transaction = tx;
                    insCmd.CommandText = @"
                        INSERT INTO Comments (Id, PostId, AuthorName, AuthorHandle, Content, TimeAgo, CreatedAt, LikeCount, ReplyCount, IsPinned, IsHearted)
                        VALUES (@Id, @PostId, @AuthorName, @AuthorHandle, @Content, @TimeAgo, @CreatedAt, @LikeCount, @ReplyCount, @IsPinned, @IsHearted);
                    ";
                    BindCommentParameters(insCmd, postId, c);
                    await insCmd.ExecuteNonQueryAsync();
                }
            }

            await tx.CommitAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task AddCommentToPostAsync(string postId, PostComment comment)
    {
        EnsureInitialized();
        await _writeLock.WaitAsync();
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Comments (Id, PostId, AuthorName, AuthorHandle, Content, TimeAgo, CreatedAt, LikeCount, ReplyCount, IsPinned, IsHearted)
                VALUES (@Id, @PostId, @AuthorName, @AuthorHandle, @Content, @TimeAgo, @CreatedAt, @LikeCount, @ReplyCount, @IsPinned, @IsHearted);
            ";
            BindCommentParameters(cmd, postId, comment);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteCommentAsync(string commentId)
    {
        EnsureInitialized();
        await _writeLock.WaitAsync();
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM Comments WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", commentId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<long> GetDatabaseSizeBytesAsync()
    {
        return await Task.Run(() =>
        {
            long size = 0;
            if (File.Exists(_databasePath)) size += new FileInfo(_databasePath).Length;
            string wal = _databasePath + "-wal";
            if (File.Exists(wal)) size += new FileInfo(wal).Length;
            string shm = _databasePath + "-shm";
            if (File.Exists(shm)) size += new FileInfo(shm).Length;
            return size;
        });
    }

    public async Task<Dictionary<string, int>> GetFolderCountsAsync()
    {
        EnsureInitialized();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT FolderName, COUNT(*) FROM Posts GROUP BY FolderName;";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }
        return counts;
    }

    public async Task<Dictionary<string, int>> GetTagCountsAsync()
    {
        var all = await GetAllPostsAsync();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in all)
        {
            if (string.IsNullOrWhiteSpace(p.Tags)) continue;
            var tags = p.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var t in tags)
            {
                string tag = t.StartsWith("#") ? t : "#" + t;
                counts[tag] = counts.TryGetValue(tag, out int c) ? c + 1 : 1;
            }
        }

        return counts;
    }

    public async Task<string> BackupDatabaseAsync(string targetFolderPath)
    {
        EnsureInitialized();
        Directory.CreateDirectory(targetFolderPath);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backupFile = Path.Combine(targetFolderPath, $"ArchiveData_Backup_{timestamp}.db");

        await _writeLock.WaitAsync();
        try
        {
            using var backupConnection = new SqliteConnection($"Data Source={backupFile};");
            await backupConnection.OpenAsync();
            _connection!.BackupDatabase(backupConnection);
            return backupFile;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SeedSampleDataIfEmptyAsync()
    {
        long count = 0;
        using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Posts;";
            var res = await cmd.ExecuteScalarAsync();
            count = res is long l ? l : Convert.ToInt64(res);
        }

        if (count > 0) return;

        string sampleMediaDir1 = Path.Combine(StoragePathHelper.MediaDirectory, $"{DateTime.Now:yyyyMMdd}_demo_sap");
        string sampleMediaDir2 = Path.Combine(StoragePathHelper.MediaDirectory, $"{DateTime.Now:yyyyMMdd}_demo_guru");
        Directory.CreateDirectory(sampleMediaDir1);
        Directory.CreateDirectory(sampleMediaDir2);

        string imgPath1 = Path.Combine(sampleMediaDir1, "preview_windows.png");
        string imgPath2 = Path.Combine(sampleMediaDir1, "preview_architecture.png");
        CreateSampleGradientBitmap(imgPath1, "Image Preview 1", "#0078D4", "#111A24");
        CreateSampleGradientBitmap(imgPath2, "Image Preview 2", "#1D9BF0", "#0D1117");

        var samplePost1 = new Post
        {
            Id = Guid.NewGuid().ToString("N"),
            AuthorName = "SAP Developers",
            AuthorHandle = "@SAPDevs",
            Content = "Clean architecture example for Windows native desktop apps using C# and high-performance SQLite engine. Perfect for ultra-fast offline indexing!\n#winui3 #sqlite #csharp",
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

    private static void BindPostParameters(SqliteCommand cmd, Post post)
    {
        cmd.Parameters.AddWithValue("@Id", post.Id);
        cmd.Parameters.AddWithValue("@AuthorName", post.AuthorName ?? "Offline User");
        cmd.Parameters.AddWithValue("@AuthorHandle", post.AuthorHandle ?? "@offline");
        cmd.Parameters.AddWithValue("@Content", post.Content ?? string.Empty);
        cmd.Parameters.AddWithValue("@PostDate", FormatDateTime(post.PostDate));
        cmd.Parameters.AddWithValue("@FolderName", post.FolderName ?? "Coding");
        cmd.Parameters.AddWithValue("@Tags", post.Tags ?? string.Empty);
        cmd.Parameters.AddWithValue("@Comment", (object?)post.Comment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FolderPath", post.FolderPath ?? string.Empty);
        cmd.Parameters.AddWithValue("@CreatedAt", FormatDateTime(post.CreatedAt));
        cmd.Parameters.AddWithValue("@ThreadId", (object?)post.ThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ThreadIndex", post.ThreadIndex);
    }

    private static void BindCommentParameters(SqliteCommand cmd, string postId, PostComment comment)
    {
        cmd.Parameters.AddWithValue("@Id", comment.Id);
        cmd.Parameters.AddWithValue("@PostId", postId);
        cmd.Parameters.AddWithValue("@AuthorName", comment.AuthorName ?? string.Empty);
        cmd.Parameters.AddWithValue("@AuthorHandle", comment.AuthorHandle ?? string.Empty);
        cmd.Parameters.AddWithValue("@Content", comment.Content ?? string.Empty);
        cmd.Parameters.AddWithValue("@TimeAgo", comment.TimeAgo ?? string.Empty);
        cmd.Parameters.AddWithValue("@CreatedAt", FormatDateTime(comment.CreatedAt));
        cmd.Parameters.AddWithValue("@LikeCount", comment.LikeCount ?? string.Empty);
        cmd.Parameters.AddWithValue("@ReplyCount", comment.ReplyCount ?? string.Empty);
        cmd.Parameters.AddWithValue("@IsPinned", comment.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("@IsHearted", comment.IsHearted ? 1 : 0);
    }

    private static string FormatDateTime(DateTime dt)
    {
        return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

    private static DateTime ParseDateTime(string raw)
    {
        if (DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.ToLocalTime();
        }
        return DateTime.Now;
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

                var pen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)), 1.0);
                for (int x = 0; x < width; x += 40)
                {
                    dc.DrawLine(pen, new System.Windows.Point(x, 0), new System.Windows.Point(x, height));
                }
                for (int y = 0; y < height; y += 40)
                {
                    dc.DrawLine(pen, new System.Windows.Point(0, y), new System.Windows.Point(width, y));
                }

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
        catch { }
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized || _connection == null)
        {
            throw new InvalidOperationException("SQLite Database Service is not initialized. Call InitializeAsync first.");
        }
    }

    public void Dispose()
    {
        try
        {
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }
        catch { }

        try
        {
            _writeLock.Dispose();
        }
        catch { }
    }
}

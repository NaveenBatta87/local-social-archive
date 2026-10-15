using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LocalSocialArchive.Models;

namespace LocalSocialArchive.Services;

public partial class TweetImporterService : ITweetImporterService
{
    private readonly IEseDatabaseService _dbService;
    private readonly string _mediaBaseDirectory;

    [GeneratedRegex(@"^\s*window\.YTD\.[A-Za-z0-9_.]+\s*=\s*", RegexOptions.Singleline)]
    private static partial Regex YtdAssignmentRegex();

    [GeneratedRegex(@"[?&](s|t|ref_src|ref_url|fbclid|gclid|igshid|mc_cid|mc_eid)=[^&#\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex TrackingQueryParamRegex();

    public TweetImporterService(IEseDatabaseService dbService)
    {
        _dbService = dbService;
        string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _mediaBaseDirectory = Path.Combine(userDocs, "LocalSocialArchive", "Media");
        Directory.CreateDirectory(_mediaBaseDirectory);
    }

    public string? DetectGalleryDlPath()
    {
        // 1. Direct user downloads location
        string userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "gallery-dl.exe");
        if (File.Exists(userDownloads)) return userDownloads;

        // 2. Specific hardcoded user downloads
        string specificPath = @"C:\Users\Naveen\Downloads\gallery-dl.exe";
        if (File.Exists(specificPath)) return specificPath;

        // 3. Application directory
        string appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gallery-dl.exe");
        if (File.Exists(appDir)) return appDir;

        // 4. System PATH search
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var p in paths)
            {
                try
                {
                    var candidate = Path.Combine(p.Trim(), "gallery-dl.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        return null;
    }

    public static string CleanTrackingUrls(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return TrackingQueryParamRegex().Replace(text, string.Empty);
    }

    public async Task<ImportResult> ImportTwitterArchiveZipAsync(
        string zipFilePath, 
        string targetFolder = "X Archive", 
        bool importLikes = false, 
        IProgress<ImportProgressReport>? progress = null)
    {
        var result = new ImportResult();
        if (!File.Exists(zipFilePath))
        {
            result.Success = false;
            result.Message = $"Archive file not found: {zipFilePath}";
            return result;
        }

        return await Task.Run(async () =>
        {
            try
            {
                progress?.Report(new ImportProgressReport { Current = 0, Total = 100, Status = "Opening Twitter/X Archive (.zip)..." });

                using var archive = ZipFile.OpenRead(zipFilePath);

                // 1. Determine account identity from account.js or manifest.js
                string defaultUsername = "me";
                string defaultDisplayName = "Archive Owner";

                var accountEntry = archive.Entries.FirstOrDefault(e => 
                    e.FullName.Equals("data/account.js", StringComparison.OrdinalIgnoreCase) || 
                    e.FullName.Equals("account.js", StringComparison.OrdinalIgnoreCase));

                if (accountEntry != null)
                {
                    try
                    {
                        using var reader = new StreamReader(accountEntry.Open(), Encoding.UTF8);
                        string rawAccount = reader.ReadToEnd();
                        string cleanJson = StripYtdWrapper(rawAccount);
                        using var doc = JsonDocument.Parse(cleanJson);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                        {
                            var firstObj = doc.RootElement[0];
                            if (firstObj.TryGetProperty("account", out var acct))
                            {
                                if (acct.TryGetProperty("username", out var u) && u.GetString() is string uStr)
                                    defaultUsername = uStr;
                                if (acct.TryGetProperty("accountDisplayName", out var d) && d.GetString() is string dStr)
                                    defaultDisplayName = dStr;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Could not parse account.js: {ex.Message}");
                    }
                }

                // 2. Map media files inside the archive (e.g., data/tweets_media/)
                // Twitter archives prefix media files with the tweet ID: "{tweet_id}-{filename}"
                var mediaEntries = archive.Entries
                    .Where(e => e.FullName.StartsWith("data/tweets_media/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.Name))
                    .GroupBy(e => ExtractTweetIdFromMediaFileName(e.Name))
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                var postsToInsert = new List<Post>();

                // 3. Read tweets.js
                var tweetsEntry = archive.Entries.FirstOrDefault(e => 
                    e.FullName.Equals("data/tweets.js", StringComparison.OrdinalIgnoreCase) || 
                    e.FullName.Equals("tweets.js", StringComparison.OrdinalIgnoreCase));

                if (tweetsEntry != null)
                {
                    progress?.Report(new ImportProgressReport { Current = 10, Total = 100, Status = "Parsing authored tweets..." });
                    using var reader = new StreamReader(tweetsEntry.Open(), Encoding.UTF8);
                    string rawTweets = reader.ReadToEnd();
                    string cleanJson = StripYtdWrapper(rawTweets);

                    using var doc = JsonDocument.Parse(cleanJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        int tweetTotal = doc.RootElement.GetArrayLength();
                        int processed = 0;

                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            processed++;
                            if (!item.TryGetProperty("tweet", out var tw)) continue;

                            string tweetId = tw.TryGetProperty("id_str", out var idStrProp) ? idStrProp.GetString() ?? "" :
                                             tw.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : Guid.NewGuid().ToString("N");

                            if (string.IsNullOrWhiteSpace(tweetId)) continue;

                            string fullText = tw.TryGetProperty("full_text", out var textProp) ? textProp.GetString() ?? "" : "";
                            fullText = CleanTrackingUrls(fullText);

                            DateTime postDate = DateTime.Now;
                            if (tw.TryGetProperty("created_at", out var dateProp) && dateProp.GetString() is string dateStr)
                            {
                                if (DateTime.TryParseExact(dateStr, "ddd MMM dd HH:mm:ss zzz yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                                {
                                    postDate = parsedDate;
                                }
                                else if (DateTime.TryParse(dateStr, out var fallbackDate))
                                {
                                    postDate = fallbackDate;
                                }
                            }

                            // Thread detection
                            string? threadId = null;
                            if (tw.TryGetProperty("in_reply_to_status_id_str", out var replyProp) && replyProp.GetString() is string repId && !string.IsNullOrWhiteSpace(repId))
                            {
                                threadId = repId;
                            }
                            else if (tw.TryGetProperty("conversation_id_str", out var convProp) && convProp.GetString() is string convId && !string.IsNullOrWhiteSpace(convId))
                            {
                                threadId = convId;
                            }

                            // Extract Hashtags
                            var tagsList = new List<string>();
                            if (tw.TryGetProperty("entities", out var entities) && entities.TryGetProperty("hashtags", out var hashtags))
                            {
                                foreach (var h in hashtags.EnumerateArray())
                                {
                                    if (h.TryGetProperty("text", out var tProp) && tProp.GetString() is string tagStr && !string.IsNullOrWhiteSpace(tagStr))
                                    {
                                        tagsList.Add("#" + tagStr.TrimStart('#'));
                                    }
                                }
                            }
                            string tagsString = string.Join(" ", tagsList);

                            var post = new Post
                            {
                                Id = tweetId,
                                AuthorName = defaultDisplayName,
                                AuthorHandle = defaultUsername.StartsWith('@') ? defaultUsername : "@" + defaultUsername,
                                Content = fullText,
                                PostDate = postDate,
                                FolderName = string.IsNullOrWhiteSpace(targetFolder) ? "X Archive" : targetFolder,
                                Tags = tagsString,
                                ThreadId = threadId,
                                CreatedAt = DateTime.Now
                            };

                            // Check and extract associated media from tweets_media
                            if (mediaEntries.TryGetValue(tweetId, out var matchedZipMedia))
                            {
                                foreach (var zipMediaEntry in matchedZipMedia)
                                {
                                    try
                                    {
                                        string destFileName = $"{tweetId}_{zipMediaEntry.Name}";
                                        string destPath = Path.Combine(_mediaBaseDirectory, destFileName);

                                        if (!File.Exists(destPath))
                                        {
                                            zipMediaEntry.ExtractToFile(destPath, overwrite: true);
                                        }

                                        var mediaItem = new MediaItem
                                        {
                                            Id = Guid.NewGuid().ToString("N"),
                                            PostId = post.Id,
                                            FilePath = destPath,
                                            MediaType = MediaItem.IsVideoExtension(destPath) ? "Video" : "Image",
                                            CreatedAt = DateTime.Now
                                        };
                                        post.MediaItems.Add(mediaItem);
                                        result.MediaCount++;
                                    }
                                    catch { }
                                }
                            }

                            postsToInsert.Add(post);

                            if (processed % 200 == 0)
                            {
                                progress?.Report(new ImportProgressReport 
                                { 
                                    Current = processed, 
                                    Total = tweetTotal, 
                                    Status = $"Extracted {processed} of {tweetTotal} tweets from archive..." 
                                });
                            }
                        }
                    }
                }

                // 4. Optionally read like.js
                if (importLikes)
                {
                    var likeEntry = archive.Entries.FirstOrDefault(e => 
                        e.FullName.Equals("data/like.js", StringComparison.OrdinalIgnoreCase) || 
                        e.FullName.Equals("like.js", StringComparison.OrdinalIgnoreCase));

                    if (likeEntry != null)
                    {
                        progress?.Report(new ImportProgressReport { Current = 50, Total = 100, Status = "Parsing liked tweets..." });
                        try
                        {
                            using var reader = new StreamReader(likeEntry.Open(), Encoding.UTF8);
                            string rawLikes = reader.ReadToEnd();
                            string cleanJson = StripYtdWrapper(rawLikes);
                            using var doc = JsonDocument.Parse(cleanJson);

                            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in doc.RootElement.EnumerateArray())
                                {
                                    if (!item.TryGetProperty("like", out var lk)) continue;

                                    string tweetId = lk.TryGetProperty("tweetId", out var idProp) ? idProp.GetString() ?? "" : Guid.NewGuid().ToString("N");
                                    string fullText = lk.TryGetProperty("fullText", out var textProp) ? textProp.GetString() ?? "" : "";
                                    string expandedUrl = lk.TryGetProperty("expandedUrl", out var urlProp) ? urlProp.GetString() ?? "" : "";

                                    var post = new Post
                                    {
                                        Id = tweetId,
                                        AuthorName = "Liked Tweet",
                                        AuthorHandle = "@x",
                                        Content = CleanTrackingUrls(fullText) + (!string.IsNullOrWhiteSpace(expandedUrl) ? $"\n\n{expandedUrl}" : ""),
                                        PostDate = DateTime.Now,
                                        FolderName = "Likes",
                                        Tags = "#like",
                                        CreatedAt = DateTime.Now
                                    };
                                    postsToInsert.Add(post);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Could not parse like.js: {ex.Message}");
                        }
                    }
                }

                // 5. Batch insert into ESE Database
                if (postsToInsert.Count > 0)
                {
                    progress?.Report(new ImportProgressReport { Current = 0, Total = postsToInsert.Count, Status = "Saving posts to local database..." });

                    var dbProgress = new Progress<(int current, int total, string status)>(p =>
                    {
                        progress?.Report(new ImportProgressReport { Current = p.current, Total = p.total, Status = p.status });
                    });

                    result.ImportedCount = await _dbService.InsertPostsBatchAsync(postsToInsert, dbProgress);
                    result.SkippedCount = postsToInsert.Count - result.ImportedCount;
                }

                result.Success = true;
                result.Message = $"Successfully imported {result.ImportedCount} tweets and {result.MediaCount} media items into '{targetFolder}'.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Import error: {ex.Message}";
                return result;
            }
        });
    }

    public async Task<ImportResult> ImportDirectoryAsync(
        string directoryPath, 
        string targetFolder = "Imported", 
        IProgress<ImportProgressReport>? progress = null)
    {
        var result = new ImportResult();
        if (!Directory.Exists(directoryPath))
        {
            result.Success = false;
            result.Message = $"Directory not found: {directoryPath}";
            return result;
        }

        return await Task.Run(async () =>
        {
            try
            {
                progress?.Report(new ImportProgressReport { Current = 0, Total = 100, Status = "Scanning directory for metadata JSON and media..." });

                // Check if this is an extracted official Twitter archive folder
                string tweetsJsPath = Path.Combine(directoryPath, "data", "tweets.js");
                if (!File.Exists(tweetsJsPath))
                {
                    tweetsJsPath = Directory.GetFiles(directoryPath, "tweets.js", SearchOption.AllDirectories).FirstOrDefault() ?? "";
                }

                if (File.Exists(tweetsJsPath))
                {
                    // It's an unzipped Twitter archive folder
                    return await ImportExtractedTwitterFolderAsync(Path.GetDirectoryName(tweetsJsPath)!, targetFolder, progress);
                }

                // Otherwise, it's a gallery-dl or scraper output folder
                var jsonFiles = Directory.GetFiles(directoryPath, "*.json", SearchOption.AllDirectories);
                var postsToInsert = new List<Post>();

                int total = jsonFiles.Length;
                int current = 0;

                foreach (var jsonFile in jsonFiles)
                {
                    current++;
                    try
                    {
                        string jsonContent = File.ReadAllText(jsonFile, Encoding.UTF8);
                        using var doc = JsonDocument.Parse(jsonContent);

                        var root = doc.RootElement;
                        if (root.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var element in root.EnumerateArray())
                            {
                                var post = ParseGalleryDlJsonElement(element, jsonFile, targetFolder, result);
                                if (post != null) postsToInsert.Add(post);
                            }
                        }
                        else if (root.ValueKind == JsonValueKind.Object)
                        {
                            var post = ParseGalleryDlJsonElement(root, jsonFile, targetFolder, result);
                            if (post != null) postsToInsert.Add(post);
                        }

                        if (current % 25 == 0)
                        {
                            progress?.Report(new ImportProgressReport 
                            { 
                                Current = current, 
                                Total = total, 
                                Status = $"Parsed {current} of {total} metadata files..." 
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Failed to parse {Path.GetFileName(jsonFile)}: {ex.Message}");
                    }
                }

                if (postsToInsert.Count > 0)
                {
                    progress?.Report(new ImportProgressReport { Current = 0, Total = postsToInsert.Count, Status = "Saving posts to local database..." });

                    var dbProgress = new Progress<(int current, int total, string status)>(p =>
                    {
                        progress?.Report(new ImportProgressReport { Current = p.current, Total = p.total, Status = p.status });
                    });

                    result.ImportedCount = await _dbService.InsertPostsBatchAsync(postsToInsert, dbProgress);
                    result.SkippedCount = postsToInsert.Count - result.ImportedCount;
                }

                result.Success = true;
                result.Message = $"Successfully imported {result.ImportedCount} posts and {result.MediaCount} media items into '{targetFolder}'.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Directory import error: {ex.Message}";
                return result;
            }
        });
    }

    private Post? ParseGalleryDlJsonElement(JsonElement root, string jsonFilePath, string targetFolder, ImportResult result)
    {
        string tweetId = "";
        if (root.TryGetProperty("tweet_id", out var tid)) tweetId = tid.ToString();
        else if (root.TryGetProperty("id", out var id)) tweetId = id.ToString();
        else tweetId = Guid.NewGuid().ToString("N");

        string content = "";
        if (root.TryGetProperty("content", out var cProp)) content = cProp.GetString() ?? "";
        else if (root.TryGetProperty("tweet_text", out var ttProp)) content = ttProp.GetString() ?? "";
        else if (root.TryGetProperty("description", out var dProp)) content = dProp.GetString() ?? "";
        else if (root.TryGetProperty("text", out var tProp)) content = tProp.GetString() ?? "";

        content = CleanTrackingUrls(content);

        // Author details
        string authorName = "Twitter User";
        string authorHandle = "@user";

        if (root.TryGetProperty("author", out var authorProp) && authorProp.ValueKind == JsonValueKind.Object)
        {
            if (authorProp.TryGetProperty("name", out var an)) authorName = an.GetString() ?? authorName;
            if (authorProp.TryGetProperty("nick", out var ah)) authorHandle = ah.GetString() ?? authorHandle;
            else if (authorProp.TryGetProperty("screen_name", out var sn)) authorHandle = sn.GetString() ?? authorHandle;
        }
        else if (root.TryGetProperty("user", out var userProp) && userProp.ValueKind == JsonValueKind.Object)
        {
            if (userProp.TryGetProperty("name", out var un)) authorName = un.GetString() ?? authorName;
            if (userProp.TryGetProperty("screen_name", out var usn)) authorHandle = usn.GetString() ?? authorHandle;
        }

        if (!authorHandle.StartsWith('@')) authorHandle = "@" + authorHandle;

        // Date
        DateTime postDate = DateTime.Now;
        if (root.TryGetProperty("date", out var dateProp))
        {
            if (DateTime.TryParse(dateProp.GetString(), out var dt)) postDate = dt;
        }
        else if (root.TryGetProperty("created_at", out var catProp))
        {
            if (DateTime.TryParse(catProp.GetString(), out var dt)) postDate = dt;
        }

        // Thread / Conversation
        string? threadId = null;
        if (root.TryGetProperty("conversation_id", out var convProp) && convProp.GetString() is string cId && !string.IsNullOrWhiteSpace(cId))
        {
            threadId = cId;
        }
        else if (root.TryGetProperty("reply_to_tweet_id", out var repProp) && repProp.GetString() is string rId && !string.IsNullOrWhiteSpace(rId))
        {
            threadId = rId;
        }

        var post = new Post
        {
            Id = tweetId,
            AuthorName = authorName,
            AuthorHandle = authorHandle,
            Content = content,
            PostDate = postDate,
            FolderName = targetFolder,
            ThreadId = threadId,
            CreatedAt = DateTime.Now
        };

        // Detect sibling media file for this JSON
        string dir = Path.GetDirectoryName(jsonFilePath) ?? "";
        string baseName = Path.GetFileNameWithoutExtension(jsonFilePath); // e.g. "image.jpg" if file was "image.jpg.json"
        
        string directMediaPath = Path.Combine(dir, baseName);
        if (File.Exists(directMediaPath) && (MediaItem.IsImageExtension(directMediaPath) || MediaItem.IsVideoExtension(directMediaPath)))
        {
            AddMediaToPost(post, directMediaPath, result);
        }
        else
        {
            // Search matching media in the same directory
            var siblingFiles = Directory.GetFiles(dir)
                .Where(f => (MediaItem.IsImageExtension(f) || MediaItem.IsVideoExtension(f)) && Path.GetFileNameWithoutExtension(f).Contains(tweetId))
                .ToList();

            foreach (var sf in siblingFiles)
            {
                AddMediaToPost(post, sf, result);
            }
        }

        return post;
    }

    private void AddMediaToPost(Post post, string sourcePath, ImportResult result)
    {
        try
        {
            string fileName = $"{post.Id}_{Path.GetFileName(sourcePath)}";
            string targetPath = Path.Combine(_mediaBaseDirectory, fileName);

            if (!File.Exists(targetPath))
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
            }

            var media = new MediaItem
            {
                Id = Guid.NewGuid().ToString("N"),
                PostId = post.Id,
                FilePath = targetPath,
                MediaType = MediaItem.IsVideoExtension(targetPath) ? "Video" : "Image",
                CreatedAt = DateTime.Now
            };

            if (!post.MediaItems.Any(m => m.FilePath == targetPath))
            {
                post.MediaItems.Add(media);
                result.MediaCount++;
            }
        }
        catch { }
    }

    private async Task<ImportResult> ImportExtractedTwitterFolderAsync(string dataDir, string targetFolder, IProgress<ImportProgressReport>? progress)
    {
        var result = new ImportResult();
        string tweetsFile = Path.Combine(dataDir, "tweets.js");
        if (!File.Exists(tweetsFile))
        {
            result.Success = false;
            result.Message = "tweets.js not found in directory.";
            return result;
        }

        string rawTweets = await File.ReadAllTextAsync(tweetsFile, Encoding.UTF8);
        string cleanJson = StripYtdWrapper(rawTweets);
        using var doc = JsonDocument.Parse(cleanJson);

        var postsToInsert = new List<Post>();
        string mediaFolder = Path.Combine(dataDir, "tweets_media");

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("tweet", out var tw)) continue;
            string tweetId = tw.TryGetProperty("id_str", out var idStr) ? idStr.GetString() ?? "" : Guid.NewGuid().ToString("N");
            string fullText = tw.TryGetProperty("full_text", out var ft) ? ft.GetString() ?? "" : "";
            fullText = CleanTrackingUrls(fullText);

            var post = new Post
            {
                Id = tweetId,
                AuthorName = "Archive Owner",
                AuthorHandle = "@me",
                Content = fullText,
                PostDate = DateTime.Now,
                FolderName = targetFolder,
                CreatedAt = DateTime.Now
            };

            if (Directory.Exists(mediaFolder))
            {
                var files = Directory.GetFiles(mediaFolder, $"{tweetId}*");
                foreach (var f in files)
                {
                    AddMediaToPost(post, f, result);
                }
            }

            postsToInsert.Add(post);
        }

        if (postsToInsert.Count > 0)
        {
            result.ImportedCount = await _dbService.InsertPostsBatchAsync(postsToInsert);
        }

        result.Success = true;
        result.Message = $"Imported {result.ImportedCount} tweets from extracted folder.";
        return result;
    }

    public async Task<ImportResult> DownloadAndImportWithGalleryDlAsync(
        string galleryDlExePath, 
        string targetUrl, 
        string targetFolder = "Downloads", 
        string? browserCookies = null, 
        int maxTweets = 50, 
        IProgress<ImportProgressReport>? progress = null, 
        Action<string>? logOutput = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ImportResult();
        if (!File.Exists(galleryDlExePath))
        {
            result.Success = false;
            result.Message = $"gallery-dl.exe was not found at: {galleryDlExePath}";
            return result;
        }

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            result.Success = false;
            result.Message = "Please specify a Twitter/X URL or username.";
            return result;
        }

        string cleanUrl = targetUrl.Trim();
        if (cleanUrl.StartsWith('@'))
        {
            cleanUrl = $"https://x.com/{cleanUrl.TrimStart('@')}";
        }
        else if (!cleanUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                 !cleanUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            cleanUrl = $"https://x.com/{cleanUrl}";
        }

        string tempDownloadDir = Path.Combine(Path.GetTempPath(), "LocalSocialArchive_Downloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDownloadDir);

        try
        {
            progress?.Report(new ImportProgressReport { Current = 10, Total = 100, Status = $"Starting gallery-dl for {cleanUrl}..." });
            logOutput?.Invoke($"Executing: gallery-dl --write-metadata -D \"{tempDownloadDir}\"");

            var argumentsBuilder = new StringBuilder();
            argumentsBuilder.Append("--write-metadata ");
            argumentsBuilder.Append($"-D \"{tempDownloadDir}\" ");

            if (maxTweets > 0)
            {
                argumentsBuilder.Append($"--range 1-{maxTweets} ");
            }

            if (!string.IsNullOrWhiteSpace(browserCookies) && !string.Equals(browserCookies, "None", StringComparison.OrdinalIgnoreCase))
            {
                argumentsBuilder.Append($"--cookies-from-browser {browserCookies.ToLowerInvariant()} ");
            }

            argumentsBuilder.Append($"\"{cleanUrl}\"");

            var psi = new ProcessStartInfo
            {
                FileName = galleryDlExePath,
                Arguments = argumentsBuilder.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    logOutput?.Invoke(e.Data);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    logOutput?.Invoke($"[WARN] {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            logOutput?.Invoke($"gallery-dl finished with exit code {process.ExitCode}.");
            progress?.Report(new ImportProgressReport { Current = 70, Total = 100, Status = "Download complete. Ingesting metadata into archive..." });

            // Now import the downloaded files into LocalSocialArchive database
            var importDirResult = await ImportDirectoryAsync(tempDownloadDir, targetFolder, progress);
            return importDirResult;
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Message = "Download operation was canceled.";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Download failed: {ex.Message}";
            return result;
        }
        finally
        {
            // Clean up temp directory
            try
            {
                if (Directory.Exists(tempDownloadDir))
                {
                    Directory.Delete(tempDownloadDir, recursive: true);
                }
            }
            catch { }
        }
    }

    private static string StripYtdWrapper(string raw)
    {
        var cleaned = YtdAssignmentRegex().Replace(raw, string.Empty).Trim();
        if (cleaned.EndsWith(';'))
        {
            cleaned = cleaned[..^1].TrimEnd();
        }
        return cleaned;
    }

    private static string ExtractTweetIdFromMediaFileName(string fileName)
    {
        int dashIdx = fileName.IndexOf('-');
        if (dashIdx > 0)
        {
            return fileName[..dashIdx];
        }
        return "";
    }
}

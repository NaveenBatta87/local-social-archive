using System;
using System.IO;

namespace LocalSocialArchive.Helpers;

/// <summary>
/// Centralized storage path manager for Local Social Archive.
/// Ensures reliable, high-performance database storage in %LOCALAPPDATA%
/// to prevent OneDrive / Cloud Files filter driver (cldflt.sys) sector-lock deadlocks,
/// with automatic migration from legacy Documents storage and portable-mode support.
/// </summary>
public static class StoragePathHelper
{
    private static readonly object _initLock = new();
    private static string? _baseDirectory;

    public static string BaseDirectory
    {
        get
        {
            if (_baseDirectory != null)
                return _baseDirectory;

            lock (_initLock)
            {
                if (_baseDirectory != null)
                    return _baseDirectory;

                string resolvedDir = DetermineStorageDirectory();
                // Assign _baseDirectory BEFORE running subdirectories creation or migration,
                // so that any logging or re-entrant calls immediately return this resolved path
                // without re-entering the initialization method and causing a StackOverflowException.
                _baseDirectory = resolvedDir;

                try
                {
                    EnsureSubdirectories(resolvedDir);
                    CheckAndMigrateLegacyData(resolvedDir);
                }
                catch (Exception ex)
                {
                    LogDiagnostic($"[StoragePathHelper Warning] Initialization issue: {ex.Message}");
                }

                return _baseDirectory;
            }
        }
    }

    public static string SqliteDatabasePath => Path.Combine(BaseDirectory, "ArchiveData.db");
    public static string LegacyEseDatabasePath => Path.Combine(BaseDirectory, "ArchiveData.edb");
    public static string DatabasePath => SqliteDatabasePath;
    public static string LogDirectory => Path.Combine(BaseDirectory, "EseLogs");
    public static string MediaDirectory => Path.Combine(BaseDirectory, "Media");
    public static string SettingsFilePath => Path.Combine(BaseDirectory, "settings.json");
    public static string SecurityConfigPath => Path.Combine(BaseDirectory, "vault_security.json");
    public static string CollectionsConfigPath => Path.Combine(BaseDirectory, "collections_config.json");
    public static string DiagnosticsLogPath => Path.Combine(BaseDirectory, "startup_diagnostics.log");

    public static void LogDiagnostic(string message)
    {
        try
        {
            // Safely use _baseDirectory if assigned; otherwise fallback directly without invoking BaseDirectory property
            string dir = _baseDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalSocialArchive");
            string path = Path.Combine(dir, "startup_diagnostics.log");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging failure should never crash the application
        }
    }

    private static string DetermineStorageDirectory()
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string portableSqliteDb = Path.Combine(appDir, "ArchiveData.db");
        string portableEseDb = Path.Combine(appDir, "ArchiveData.edb");
        string portableDataDir = Path.Combine(appDir, "Data");
        string portableDataSqliteDb = Path.Combine(portableDataDir, "ArchiveData.db");
        string portableDataEseDb = Path.Combine(portableDataDir, "ArchiveData.edb");

        // 1. Portable Mode: Check if database exists directly next to executable
        // (Ensure it's not in Program Files where non-admin write fails)
        bool isProgramFiles = appDir.Contains("Program Files", StringComparison.OrdinalIgnoreCase);

        if (!isProgramFiles && (File.Exists(portableSqliteDb) || File.Exists(portableEseDb)))
        {
            return appDir;
        }

        if (!isProgramFiles && (File.Exists(portableDataSqliteDb) || File.Exists(portableDataEseDb) || Directory.Exists(portableDataDir)))
        {
            return portableDataDir;
        }

        // 2. Primary Standard Location: %LOCALAPPDATA%\LocalSocialArchive
        // %LOCALAPPDATA% is guaranteed local (no OneDrive sync, no cldflt.sys lock collisions)
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string primaryDir = Path.Combine(localAppData, "LocalSocialArchive");
        return primaryDir;
    }

    private static void CheckAndMigrateLegacyData(string primaryDir)
    {
        try
        {
            string primarySqliteDb = Path.Combine(primaryDir, "ArchiveData.db");
            string primaryEseDb = Path.Combine(primaryDir, "ArchiveData.edb");

            string legacyDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string legacyDir = Path.Combine(legacyDocs, "LocalSocialArchive");
            string legacySqliteDb = Path.Combine(legacyDir, "ArchiveData.db");
            string legacyEseDb = Path.Combine(legacyDir, "ArchiveData.edb");

            if (!File.Exists(primarySqliteDb) && !File.Exists(primaryEseDb) && (File.Exists(legacySqliteDb) || File.Exists(legacyEseDb)))
            {
                MigrateLegacyData(legacyDir, primaryDir);
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"[StoragePathHelper Migration Warning] {ex.Message}");
        }
    }

    private static void EnsureSubdirectories(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "EseLogs"));
            Directory.CreateDirectory(Path.Combine(dir, "Media"));
        }
        catch
        {
            // Directory creation will be surfaced during operation if unauthorized
        }
    }

    private static void MigrateLegacyData(string legacyDir, string targetDir)
    {
        LogDiagnostic($"Initiating automatic migration from legacy '{legacyDir}' to '{targetDir}'...");

        // 1. Copy SQLite database file if present
        string legacySqliteDb = Path.Combine(legacyDir, "ArchiveData.db");
        string targetSqliteDb = Path.Combine(targetDir, "ArchiveData.db");
        if (File.Exists(legacySqliteDb) && !File.Exists(targetSqliteDb))
        {
            try
            {
                File.Copy(legacySqliteDb, targetSqliteDb, overwrite: false);
                LogDiagnostic("Migrated ArchiveData.db successfully.");
            }
            catch (Exception ex)
            {
                LogDiagnostic($"[ArchiveData.db migration warning] {ex.Message}");
            }
        }

        // 2. Copy legacy ESE database file if present
        string legacyEseDb = Path.Combine(legacyDir, "ArchiveData.edb");
        string targetEseDb = Path.Combine(targetDir, "ArchiveData.edb");
        if (File.Exists(legacyEseDb) && !File.Exists(targetEseDb))
        {
            try
            {
                File.Copy(legacyEseDb, targetEseDb, overwrite: false);
                LogDiagnostic("Migrated ArchiveData.edb successfully.");
            }
            catch (Exception ex)
            {
                LogDiagnostic($"[ArchiveData.edb migration warning] {ex.Message}");
            }
        }

        // 2. Copy configuration files
        string[] configFiles = { "collections_config.json", "settings.json", "vault_security.json" };
        foreach (var file in configFiles)
        {
            string src = Path.Combine(legacyDir, file);
            string dst = Path.Combine(targetDir, file);
            if (File.Exists(src) && !File.Exists(dst))
            {
                try
                {
                    File.Copy(src, dst, overwrite: false);
                    LogDiagnostic($"Migrated {file} successfully.");
                }
                catch { }
            }
        }

        // 3. Copy EseLogs if present (needed for ESE soft recovery)
        string legacyEseLogs = Path.Combine(legacyDir, "EseLogs");
        string targetEseLogs = Path.Combine(targetDir, "EseLogs");
        if (Directory.Exists(legacyEseLogs))
        {
            try
            {
                CopyDirectoryRecursive(legacyEseLogs, targetEseLogs);
                LogDiagnostic("Migrated EseLogs directory successfully.");
            }
            catch (Exception ex)
            {
                LogDiagnostic($"[EseLogs migration notice] {ex.Message}");
            }
        }

        // 4. Copy Media items if present
        string legacyMedia = Path.Combine(legacyDir, "Media");
        string targetMedia = Path.Combine(targetDir, "Media");
        if (Directory.Exists(legacyMedia))
        {
            try
            {
                CopyDirectoryRecursive(legacyMedia, targetMedia);
                LogDiagnostic("Migrated Media directory successfully.");
            }
            catch (Exception ex)
            {
                LogDiagnostic($"[Media migration notice] {ex.Message}");
            }
        }

        LogDiagnostic("Migration finished.");
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(targetDir, Path.GetFileName(file));
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile, overwrite: false);
            }
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSubDir);
        }
    }
}

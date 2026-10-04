namespace LocalSocialArchive.Helpers;

/// <summary>
/// VS Code Codicons Unicode Character Mapping (Open Source MIT)
/// </summary>
public static class Codicons
{
    public const string Add = "\uea60";
    public const string Lightbulb = "\uea61";
    public const string Tag = "\uea66";
    public const string Comment = "\uea6b";
    public const string Search = "\uea6d";
    public const string Eye = "\uea70";
    public const string Edit = "\uea73";
    public const string Info = "\uea74";
    public const string Lock = "\uea75";
    public const string Unlock = "\ueb11"; // Key
    public const string Close = "\uea76";
    public const string Sync = "\uea77";
    public const string Trash = "\uea81";
    public const string History = "\uea82";
    public const string Folder = "\uea83";
    public const string FolderOpened = "\ueaf7";
    public const string FolderLibrary = "\uebdf";
    public const string SymbolFile = "\uea7b";
    public const string ArrowLeft = "\uea9b";
    public const string ArrowRight = "\uea9c";
    public const string ArrowUp = "\ueaa1";
    public const string ArrowDown = "\uea9a";
    public const string ArrowSwap = "\uebcb";
    public const string Bookmark = "\ueaa5";
    public const string Book = "\ueaa4";
    public const string Bold = "\ueaa3";
    public const string Italic = "\ueb0d";
    public const string Check = "\ueab2";
    public const string CheckAll = "\uebb1";
    public const string ChevronDown = "\ueab4";
    public const string ChevronLeft = "\ueab5";
    public const string ChevronRight = "\ueab6";
    public const string ChevronUp = "\ueab7";
    public const string ClearAll = "\ueabf";
    public const string Filter = "\ueaf1";
    public const string Gear = "\ueaf8";
    public const string Home = "\ueb06";
    public const string Key = "\ueb11";
    public const string Link = "\ueb15";
    public const string LinkExternal = "\ueb14";
    public const string ListUnordered = "\ueb17";
    public const string ListOrdered = "\ueb16";
    public const string ListSelection = "\ueb85";
    public const string ListTree = "\ueb86";
    public const string Copy = "\uebcc";
    public const string Database = "\ueace";
    public const string DeviceCamera = "\ueada";
    public const string DeviceCameraVideo = "\uead9";
    public const string FileMedia = "\ueaea";
    public const string FileText = "\uec5e";
    public const string FileCode = "\ueae9";
    public const string Play = "\ueb2c";
    public const string DebugPause = "\uead1";
    public const string ScreenFull = "\ueb4c";
    public const string ScreenNormal = "\ueb4d";
    public const string Mute = "\ueb24";
    public const string Unmute = "\ueb75";
    public const string CloudDownload = "\ueac2";
    public const string CloudUpload = "\ueac3";
    public const string Export = "\uebac";
    public const string Layout = "\uebeb";
    public const string Layers = "\uebd2";
    public const string Sparkle = "\uec10";
    public const string Quotes = "\uec60";
    public const string Note = "\uebaf";
    public const string Pin = "\ueb2b";
    public const string Unpin = "\uec9e";
    public const string KebabVertical = "\ueb10";
    public const string KebabHorizontal = "\uea7c";
    public const string Heart = "\ueb05";
    public const string HeartFilled = "\uec04";
    public const string Globe = "\ueb01";
    public const string Combine = "\uebb6";
    public const string Diff = "\ueae1";
    public const string Refresh = "\ueb37";

    /// <summary>
    /// Converts existing emoji strings or names to corresponding Codicon unicode glyphs.
    /// </summary>
    public static string ToCodicon(string? glyph)
    {
        if (string.IsNullOrWhiteSpace(glyph)) return Folder;

        string trimmed = glyph.Trim();
        if (trimmed.Length == 1 && trimmed[0] >= 0xEA00 && trimmed[0] <= 0xEFFF)
        {
            return trimmed; // Already a Codicon character
        }

        return trimmed switch
        {
            "📁" or "folder" => Folder,
            "📂" or "open-folder" => FolderOpened,
            "🗂️" or "layers" => Layers,
            "💻" or "code" => FileCode,
            "🏢" or "database" => Database,
            "⭐" or "sparkle" => Sparkle,
            "🏷️" or "tag" => Tag,
            "❤️" or "heart" => Heart,
            "💬" or "comment" => Comment,
            "🔍" or "search" => Search,
            "⚙️" or "settings" or "gear" => Gear,
            "🔒" or "lock" => Lock,
            "🔓" or "unlock" or "key" => Unlock,
            "✏️" or "edit" => Edit,
            "🗑️" or "delete" or "trash" => Trash,
            "📋" or "copy" => Copy,
            "⤢" or "fullscreen" => ScreenFull,
            "🔗" or "link" => Link,
            "✓" or "check" => Check,
            "☑" or "check-all" => CheckAll,
            "✕" or "close" => Close,
            "◀" or "prev" => ChevronLeft,
            "▶" or "next" => ChevronRight,
            "▼" or "⌵" or "down" => ChevronDown,
            "▲" or "up" => ChevronUp,
            "➕" or "add" => Add,
            "📸" or "photo" => DeviceCamera,
            "🎞️" or "video" => DeviceCameraVideo,
            "ℹ️" or "info" => Info,
            "🔄" or "refresh" => Refresh,
            "🏠" or "home" => Home,
            "📖" or "book" => Book,
            "📝" or "note" => FileText,
            "🗃️" or "archive" => FolderLibrary,
            _ => Folder
        };
    }
}

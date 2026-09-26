using System.Runtime.InteropServices;

namespace LocalSocialArchive.Helpers;

public class EmojiCategory
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<string> Emojis { get; set; } = new();
}

public static class EmojiPacks
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_LWIN = 0x5B;
    private const byte VK_OEM_PERIOD = 0xBE;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// Invokes the native Windows built-in Emoji & Symbol Picker (Win + .) at the current focused caret.
    /// </summary>
    public static void OpenWindowsEmojiPicker()
    {
        try
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_OEM_PERIOD, 0, 0, UIntPtr.Zero);
            keybd_event(VK_OEM_PERIOD, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch { }
    }
    public static List<EmojiCategory> Categories { get; } = new()
    {
        new EmojiCategory
        {
            Name = "Popular",
            Icon = "🔥",
            Emojis = new List<string>
            {
                "👍", "🔥", "🚀", "❤️", "🎉", "✨", "💡", "💯",
                "👏", "🙌", "🎯", "⚡", "🌟", "🤩", "🥳", "💎",
                "✅", "❌", "💬", "🧵", "📌", "📁", "💻", "☕"
            }
        },
        new EmojiCategory
        {
            Name = "Smileys",
            Icon = "😀",
            Emojis = new List<string>
            {
                "😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣",
                "😊", "😇", "🙂", "🙃", "😉", "😌", "😍", "🥰",
                "😘", "😋", "😛", "😜", "🤪", "😎", "🤓", "🧐",
                "🤔", "🤫", "😴", "🤯", "🥳", "🥺", "🤠", "🤖"
            }
        },
        new EmojiCategory
        {
            Name = "Tech & Dev",
            Icon = "💻",
            Emojis = new List<string>
            {
                "💻", "🖥️", "📱", "💾", "💽", "🖱️", "⌨️", "📡",
                "⚙️", "🛠️", "🔧", "🔨", "⚡", "🔌", "🔋", "🔒",
                "🔑", "🛡️", "📦", "🌐", "🚀", "🧪", "🧬", "📊",
                "📈", "📉", "🤖", "🧠", "💡", "☕", "🕹️", "🏗️"
            }
        },
        new EmojiCategory
        {
            Name = "Notes & Social",
            Icon = "📝",
            Emojis = new List<string>
            {
                "💬", "💭", "📢", "📣", "🔔", "✉️", "📧", "📝",
                "📄", "📑", "📌", "📎", "🏷️", "📁", "📂", "🗂️",
                "🧵", "🔗", "📰", "🔍", "🔎", "✍️", "📖", "📚",
                "🎨", "🎬", "📷", "🎥", "📻", "🎵", "🏆", "🎯"
            }
        },
        new EmojiCategory
        {
            Name = "Symbols",
            Icon = "⭐️",
            Emojis = new List<string>
            {
                "✅", "❌", "⚠️", "⛔", "🚫", "🔴", "🟢", "🔵",
                "🟡", "🟣", "⚪", "⚫", "⭐️", "🌟", "💥", "💫",
                "✨", "🏆", "🥇", "🥈", "🥉", "💯", "ℹ️", "❓",
                "❗️", "➕", "➖", "➡️", "⬅️", "⬆️", "⬇️", "🔀"
            }
        }
    };
}

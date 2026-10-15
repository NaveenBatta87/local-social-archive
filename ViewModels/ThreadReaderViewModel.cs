using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using LocalSocialArchive.Models;

namespace LocalSocialArchive.ViewModels;

public partial class ThreadReaderViewModel : ObservableObject
{
    public ObservableCollection<Post> Posts { get; } = new();

    [ObservableProperty]
    private string _threadId = string.Empty;

    [ObservableProperty]
    private string _authorName = "Author";

    [ObservableProperty]
    private string _authorHandle = "@author";

    [ObservableProperty]
    private string _authorAvatarInitial = "?";

    [ObservableProperty]
    private string _folderName = "Threads";

    [ObservableProperty]
    private string _postDateText = string.Empty;

    [ObservableProperty]
    private string _storyTitle = "Thread Story";

    [ObservableProperty]
    private int _totalTweetsCount;

    [ObservableProperty]
    private int _totalWordCount;

    [ObservableProperty]
    private string _readingTimeText = "1 min read";

    [ObservableProperty]
    private double _readerFontSize = 15.5;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _activeTheme = "Dark Slate"; // "Dark Slate", "OLED Black", "Sepia"

    [ObservableProperty]
    private ObservableCollection<string> _availableFolders = new();

    private readonly Func<string, Task>? _onMoveFolder;

    public ThreadReaderViewModel(IEnumerable<Post> threadPosts, IEnumerable<string>? availableFolders = null, Func<string, Task>? onMoveFolder = null)
    {
        _onMoveFolder = onMoveFolder;
        if (availableFolders != null)
        {
            foreach (var f in availableFolders)
            {
                AvailableFolders.Add(f);
            }
        }

        var ordered = threadPosts.OrderBy(p => p.ThreadIndex).ToList();
        foreach (var p in ordered)
        {
            Posts.Add(p);
        }

        TotalTweetsCount = Posts.Count;

        if (Posts.Count > 0)
        {
            var first = Posts[0];
            ThreadId = first.ThreadId ?? string.Empty;
            AuthorName = !string.IsNullOrWhiteSpace(first.AuthorName) ? first.AuthorName : "Author";
            AuthorHandle = !string.IsNullOrWhiteSpace(first.AuthorHandle) ? first.AuthorHandle : "@author";
            AuthorAvatarInitial = first.AvatarInitial;
            FolderName = first.FolderName;
            PostDateText = first.PostDate.ToString("MMMM dd, yyyy • h:mm tt");

            // Extract a title from the first post
            var firstLine = first.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstLine))
            {
                StoryTitle = firstLine.Length > 80 ? firstLine.Substring(0, 77) + "..." : firstLine;
            }
            else
            {
                StoryTitle = $"Thread by {AuthorName}";
            }

            int words = 0;
            foreach (var p in Posts)
            {
                words += p.Content.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }
            TotalWordCount = words;
            int mins = Math.Max(1, (int)Math.Ceiling(words / 200.0));
            ReadingTimeText = $"{mins} min read";
        }
    }

    [RelayCommand]
    public void ZoomInFont()
    {
        ReaderFontSize = Math.Min(24.0, ReaderFontSize + 1.5);
    }

    [RelayCommand]
    public void ZoomOutFont()
    {
        ReaderFontSize = Math.Max(12.0, ReaderFontSize - 1.5);
    }

    [RelayCommand]
    public void ResetFont()
    {
        ReaderFontSize = 15.5;
    }

    [RelayCommand]
    public void CycleTheme()
    {
        ActiveTheme = ActiveTheme switch
        {
            "Dark Slate" => "OLED Black",
            "OLED Black" => "Sepia",
            _ => "Dark Slate"
        };
        StatusMessage = $"Reader theme: {ActiveTheme}";
    }

    [RelayCommand]
    public void CopyStory()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {StoryTitle}");
        sb.AppendLine($"**Author:** {AuthorName} ({AuthorHandle})  ");
        sb.AppendLine($"**Date:** {PostDateText}  ");
        sb.AppendLine($"**Tweets:** {TotalTweetsCount} | **Reading time:** {ReadingTimeText}\n");
        sb.AppendLine("---\n");

        for (int i = 0; i < Posts.Count; i++)
        {
            var p = Posts[i];
            sb.AppendLine($"**[{i + 1}/{Posts.Count}]**\n");
            sb.AppendLine(p.Content);
            if (!string.IsNullOrWhiteSpace(p.Comment))
            {
                sb.AppendLine($"\n> *Note: {p.Comment}*");
            }
            sb.AppendLine("\n---\n");
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            StatusMessage = "Copied entire thread story to clipboard!";
            MessageBox.Show("Entire thread story copied to clipboard as Markdown!", "Thread Reader", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            StatusMessage = "Failed to copy to clipboard.";
        }
    }

    [RelayCommand]
    public void ExportMarkdown()
    {
        var sfd = new SaveFileDialog
        {
            Title = "Export Thread as Markdown Document",
            Filter = "Markdown Document (*.md)|*.md|Text Document (*.txt)|*.txt",
            FileName = $"{SanitizeFileName(StoryTitle)}.md"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# {StoryTitle}\n");
                sb.AppendLine($"> **Author:** {AuthorName} ({AuthorHandle})  ");
                sb.AppendLine($"> **Archived Date:** {PostDateText}  ");
                sb.AppendLine($"> **Folder:** {FolderName} | **Parts:** {TotalTweetsCount}\n");
                sb.AppendLine("---\n");

                for (int i = 0; i < Posts.Count; i++)
                {
                    var p = Posts[i];
                    sb.AppendLine($"### Tweet {i + 1} of {Posts.Count}\n");
                    sb.AppendLine(p.Content + "\n");

                    if (p.MediaItems.Count > 0)
                    {
                        foreach (var m in p.MediaItems)
                        {
                            if (!string.IsNullOrEmpty(m.FilePath))
                            {
                                sb.AppendLine($"![Media]({m.FilePath})\n");
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(p.Comment))
                    {
                        sb.AppendLine($"> 💬 Note: {p.Comment}\n");
                    }

                    sb.AppendLine("---\n");
                }

                sb.AppendLine($"\n*Archived locally with Local Social Archive.*");
                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                StatusMessage = $"Saved thread to {Path.GetFileName(sfd.FileName)}";
                MessageBox.Show($"Successfully exported thread to:\n{sfd.FileName}", "Export Completed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    public void OpenStoryExport()
    {
        var vm = new StoryExportViewModel(initialPosts: Posts);
        var win = new Views.StoryExportWindow(vm);
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        if (activeWindow != null && activeWindow.IsVisible)
        {
            win.Owner = activeWindow;
        }
        win.ShowDialog();
    }

    [RelayCommand]
    public void OpenDocumentReader()
    {
        var vm = new StoryDocumentReaderViewModel(Posts);
        var win = new Views.StoryDocumentReaderWindow(vm);
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        if (activeWindow != null && activeWindow.IsVisible)
        {
            win.Owner = activeWindow;
        }
        win.Show();
    }

    [RelayCommand]
    public async Task MoveToFolderAsync(string? newFolder)
    {
        if (string.IsNullOrWhiteSpace(newFolder) || newFolder == FolderName) return;
        if (_onMoveFolder != null)
        {
            await _onMoveFolder(newFolder);
        }
        FolderName = newFolder;
        foreach (var p in Posts)
        {
            p.FolderName = newFolder;
        }
        StatusMessage = $"Thread moved to folder '{newFolder}'.";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "Thread_Story" : clean;
    }
}

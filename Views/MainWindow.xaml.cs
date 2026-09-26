using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.Models;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _videoTimer;
    private bool _isVideoPlaying;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _videoTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _videoTimer.Tick += VideoTimer_Tick;

        // Intercept native paste in Word Canvas to handle DIBs (MS Word/Paint), browser PNGs, and dropped media
        DataObject.AddPastingHandler(DocRichTextBox, OnDocRichTextBoxPasting);

        Loaded += async (s, e) =>
        {
            await _viewModel.InitializeAsync();
        };

        // When Theater mode or media changes, manage video player lifecycle
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsTheaterModeOpen) or nameof(MainViewModel.CurrentTheaterMedia))
            {
                if (!_viewModel.IsTheaterModeOpen || !_viewModel.IsTheaterMediaVideo)
                {
                    StopVideo();
                }
                else
                {
                    // Auto-play video when entering theater mode on video media
                    PlayVideo();
                }
            }
        };
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        StopVideo();
        Close();
    }

    private void FeedListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedPost != null)
        {
            _viewModel.OpenTheaterModeCommand.Execute(_viewModel.SelectedPost);
        }
    }

    // ========== VIDEO PLAYER CONTROLS ==========
    private void PlayVideo()
    {
        try
        {
            TheaterVideoPlayer.Play();
            _isVideoPlaying = true;
            TxtPlayIcon.Text = "⏸";
            _videoTimer.Start();
        }
        catch { }
    }

    private void PauseVideo()
    {
        try
        {
            TheaterVideoPlayer.Pause();
            _isVideoPlaying = false;
            TxtPlayIcon.Text = "▶";
            _videoTimer.Stop();
        }
        catch { }
    }

    private void StopVideo()
    {
        try
        {
            TheaterVideoPlayer.Stop();
            TheaterVideoPlayer.Close();
            _isVideoPlaying = false;
            TxtPlayIcon.Text = "▶";
            _videoTimer.Stop();
            VideoProgressSlider.Value = 0;
            TxtVideoTime.Text = "00:00 / 00:00";
        }
        catch { }
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isVideoPlaying)
        {
            PauseVideo();
        }
        else
        {
            PlayVideo();
        }
    }

    private void BtnStopVideo_Click(object sender, RoutedEventArgs e)
    {
        StopVideo();
    }

    private void TheaterVideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (TheaterVideoPlayer.NaturalDuration.HasTimeSpan)
        {
            var duration = TheaterVideoPlayer.NaturalDuration.TimeSpan;
            VideoProgressSlider.Maximum = duration.TotalSeconds;
            TxtVideoTime.Text = $"00:00 / {FormatTimeSpan(duration)}";
            PlayVideo();
        }
    }

    private void TheaterVideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        TheaterVideoPlayer.Stop();
        _isVideoPlaying = false;
        TxtPlayIcon.Text = "▶";
        _videoTimer.Stop();
        VideoProgressSlider.Value = 0;
    }

    private void VideoTimer_Tick(object? sender, EventArgs e)
    {
        if (TheaterVideoPlayer.NaturalDuration.HasTimeSpan)
        {
            var current = TheaterVideoPlayer.Position;
            var total = TheaterVideoPlayer.NaturalDuration.TimeSpan;
            VideoProgressSlider.Value = current.TotalSeconds;
            TxtVideoTime.Text = $"{FormatTimeSpan(current)} / {FormatTimeSpan(total)}";
        }
    }

    private void VideoProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Math.Abs(e.NewValue - TheaterVideoPlayer.Position.TotalSeconds) > 1.0)
        {
            TheaterVideoPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
        }
    }

    private void VideoVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        TheaterVideoPlayer.Volume = e.NewValue;
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        return ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
    }

    // ========== MICROSOFT WORD DOCUMENT CANVAS METHODS ==========
    private void OnDocRichTextBoxPasting(object sender, DataObjectPastingEventArgs e)
    {
        try
        {
            // 1. Check if media files were pasted (images/videos/jfif/etc.)
            var mediaFiles = ClipboardHelper.GetMediaFilesFromClipboard(e.DataObject);
            if (mediaFiles.Count > 0)
            {
                e.CancelCommand();
                e.Handled = true;
                foreach (var file in mediaFiles)
                {
                    InsertMediaIntoFlowDocument(file);
                }
                return;
            }

            // 2. Check if an image or DIB (from MS Word, MS Paint, Chrome/Edge PNG) was pasted
            var bmp = ClipboardHelper.GetImageFromClipboard(e.DataObject);
            if (bmp != null)
            {
                e.CancelCommand();
                e.Handled = true;

                string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string stagingDir = Path.Combine(userDocs, "LocalSocialArchive", "Media", "CanvasStaging");
                Directory.CreateDirectory(stagingDir);
                string imgPath = Path.Combine(stagingDir, $"pasted_img_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}.png");

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(imgPath))
                {
                    encoder.Save(fs);
                }
                InsertMediaIntoFlowDocument(imgPath);
                return;
            }
        }
        catch { }
    }

    private void BtnInsertImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Insert Image or GIF into Document",
            Filter = "Images & GIFs (*.png;*.jpg;*.jpeg;*.jfif;*.jif;*.webp;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.jfif;*.jif;*.webp;*.bmp;*.gif|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() == true)
        {
            foreach (var file in dlg.FileNames)
            {
                InsertMediaIntoFlowDocument(file);
            }
        }
    }

    private void BtnInsertVideo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Insert Video into Document",
            Filter = "Video Files (*.mp4;*.webm;*.mov;*.mkv;*.avi;*.wmv)|*.mp4;*.webm;*.mov;*.mkv;*.avi;*.wmv|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() == true)
        {
            foreach (var file in dlg.FileNames)
            {
                InsertMediaIntoFlowDocument(file);
            }
        }
    }

    private void BtnDocEmoji_Click(object sender, RoutedEventArgs e)
    {
        DocEmojiPopup.IsOpen = !DocEmojiPopup.IsOpen;
    }

    private void BtnOpenWindowsEmojiPicker_Click(object sender, RoutedEventArgs e)
    {
        DocEmojiPopup.IsOpen = false;
        DocRichTextBox.Focus();
        EmojiPacks.OpenWindowsEmojiPicker();
    }

    private void BtnNodeWindowsEmojiPicker_Click(object sender, RoutedEventArgs e)
    {
        EmojiPacks.OpenWindowsEmojiPicker();
    }

    private void BtnDocEmojiItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string emoji)
        {
            if (DocRichTextBox.CaretPosition != null)
            {
                DocRichTextBox.CaretPosition.InsertTextInRun(emoji);
            }
            else
            {
                DocRichTextBox.AppendText(emoji);
            }
            DocEmojiPopup.IsOpen = false;
            DocRichTextBox.Focus();
        }
    }

    private void BtnPasteIntoDoc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var mediaFiles = ClipboardHelper.GetMediaFilesFromClipboard();
            if (mediaFiles.Count > 0)
            {
                foreach (var f in mediaFiles)
                {
                    InsertMediaIntoFlowDocument(f);
                }
                return;
            }

            var bmp = ClipboardHelper.GetImageFromClipboard();
            if (bmp != null)
            {
                string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string stagingDir = Path.Combine(userDocs, "LocalSocialArchive", "Media", "CanvasStaging");
                Directory.CreateDirectory(stagingDir);
                string imgPath = Path.Combine(stagingDir, $"pasted_img_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}.png");

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(imgPath))
                {
                    encoder.Save(fs);
                }
                InsertMediaIntoFlowDocument(imgPath);
                return;
            }

            DocRichTextBox.Paste();
        }
        catch
        {
            DocRichTextBox.Paste();
        }
    }

    private void InsertMediaIntoFlowDocument(string filePath)
    {
        if (!File.Exists(filePath)) return;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        bool isVideo = ext is ".mp4" or ".webm" or ".avi" or ".mov";

        FrameworkElement contentElement;
        if (isVideo)
        {
            var media = new MediaElement
            {
                Source = new Uri(filePath, UriKind.Absolute),
                LoadedBehavior = MediaState.Play,
                UnloadedBehavior = MediaState.Manual,
                Width = 540,
                Height = 310,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 4, 0, 4)
            };
            contentElement = media;
        }
        else
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(filePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            var img = new Image
            {
                Source = bmp,
                MaxWidth = 580,
                MaxHeight = 440,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 4, 0, 4)
            };
            contentElement = img;
        }

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(12, 16, 23)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(47, 51, 54)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 8, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = contentElement,
            Tag = filePath
        };

        var container = new BlockUIContainer(border);
        DocRichTextBox.Document.Blocks.Add(container);
    }

    private async void BtnArchiveDoc_Click(object sender, RoutedEventArgs e)
    {
        var (content, mediaList) = ExtractContentFromDoc(DocRichTextBox.Document);

        var post = await _viewModel.ArchiveWordDocumentAsync(
            _viewModel.CanvasThreadTitle,
            _viewModel.CanvasSelectedFolder,
            _viewModel.CanvasDefaultAuthor,
            _viewModel.CanvasDefaultHandle,
            _viewModel.CanvasDefaultTags,
            content,
            mediaList
        );

        if (post != null)
        {
            var res = MessageBox.Show(
                $"Document successfully saved and archived into folder '{post.FolderName}'!\n\nWould you like to clear the document canvas for a new note?",
                "Document Archived",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes)
            {
                ClearDocument();
            }
        }
    }

    private void BtnClearDoc_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("Are you sure you want to clear this document canvas?", "Clear Canvas", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            ClearDocument();
        }
    }

    private void ClearDocument()
    {
        DocRichTextBox.Document.Blocks.Clear();
        DocRichTextBox.Document.Blocks.Add(new Paragraph());
        _viewModel.CanvasThreadTitle = "Untitled Document";
    }

    private static (string text, List<string> mediaPaths) ExtractContentFromDoc(FlowDocument doc)
    {
        var sb = new StringBuilder();
        var mediaPaths = new List<string>();

        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph p)
            {
                var tr = new TextRange(p.ContentStart, p.ContentEnd);
                var text = tr.Text.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                    sb.AppendLine();
                }

                foreach (var inline in p.Inlines)
                {
                    if (inline is InlineUIContainer uic && uic.Child is FrameworkElement fe && fe.Tag is string pth && File.Exists(pth))
                    {
                        if (!mediaPaths.Contains(pth)) mediaPaths.Add(pth);
                    }
                }
            }
            else if (block is BlockUIContainer buic && buic.Child is FrameworkElement fe && fe.Tag is string path && File.Exists(path))
            {
                if (!mediaPaths.Contains(path)) mediaPaths.Add(path);
            }
            else if (block is List list)
            {
                foreach (var item in list.ListItems)
                {
                    var tr = new TextRange(item.ContentStart, item.ContentEnd);
                    sb.AppendLine($"- {tr.Text.Trim()}");
                }
                sb.AppendLine();
            }
        }

        return (sb.ToString().Trim(), mediaPaths);
    }
}

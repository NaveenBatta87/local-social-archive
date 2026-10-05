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
using System.Windows.Interop;
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
            var source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
        };

        _viewModel.RequestClearSelection += () =>
        {
            FeedListControl.SelectedItems.Clear();
            FeedGridControl.SelectedItems.Clear();
        };

        _viewModel.RequestSelectAll += () =>
        {
            if (FeedListControl.IsVisible)
            {
                FeedListControl.SelectAll();
                _viewModel.UpdateSelectedPosts(FeedListControl.SelectedItems.Cast<Post>().ToList());
            }
            else if (FeedGridControl.IsVisible)
            {
                FeedGridControl.SelectAll();
                _viewModel.UpdateSelectedPosts(FeedGridControl.SelectedItems.Cast<Post>().ToList());
            }
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
            else if (e.PropertyName == nameof(MainViewModel.IsGridView))
            {
                // Synchronize selection between List view and Grid view
                var currentSelected = _viewModel.SelectedPosts.ToList();
                var targetList = _viewModel.IsGridView ? FeedGridControl : FeedListControl;
                targetList.SelectedItems.Clear();
                foreach (var item in currentSelected)
                {
                    targetList.SelectedItems.Add(item);
                }
            }
        };
    }

    private void FeedListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView lv)
        {
            var selected = lv.SelectedItems.Cast<Post>().ToList();
            _viewModel.UpdateSelectedPosts(selected);
        }
    }

    private void FeedListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            if (_viewModel.DeleteSelectedPostsCommand.CanExecute(null))
            {
                _viewModel.DeleteSelectedPostsCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.ClearSelection();
            e.Handled = true;
        }
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

    // ========== THEATER TOUCHPAD & GESTURE CONTROLS ==========
    private const int WM_MOUSEHWHEEL = 0x020E;
    private int _swipeAccumulator;
    private DateTime _lastSwipeTime = DateTime.MinValue;
    private double _manipulationTranslationX;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEHWHEEL && _viewModel.IsTheaterModeOpen)
        {
            short delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            HandleTouchpadHorizontalScroll(delta);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void HandleTouchpadHorizontalScroll(short delta)
    {
        if (delta == 0) return;

        if (_viewModel.IsTheaterMediaVideo)
        {
            // Touchpad 2-finger horizontal swipe/scroll: scrub video forward / backward
            if (TheaterVideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var duration = TheaterVideoPlayer.NaturalDuration.TimeSpan;
                double stepSeconds = delta > 0 ? 5.0 : -5.0;
                var target = TheaterVideoPlayer.Position + TimeSpan.FromSeconds(stepSeconds);
                if (target < TimeSpan.Zero) target = TimeSpan.Zero;
                if (target > duration) target = duration;
                TheaterVideoPlayer.Position = target;
                VideoProgressSlider.Value = target.TotalSeconds;
                TxtVideoTime.Text = $"{FormatTimeSpan(target)} / {FormatTimeSpan(duration)}";
            }
        }
        else
        {
            // Touchpad 2-finger horizontal swipe: course between images
            if ((DateTime.Now - _lastSwipeTime).TotalMilliseconds < 250) return;

            _swipeAccumulator += delta;
            if (Math.Abs(_swipeAccumulator) >= 60)
            {
                if (_swipeAccumulator > 0)
                {
                    // Swiping right -> Next Image or next linked story part
                    if (_viewModel.HasMultipleTheaterMedia)
                        _viewModel.NextTheaterMediaCommand.Execute(null);
                    else if (_viewModel.TheaterPost?.IsPartOfThread == true)
                        _viewModel.SelectNextLinkedPartCommand.Execute(null);
                }
                else
                {
                    // Swiping left -> Previous Image or previous linked story part
                    if (_viewModel.HasMultipleTheaterMedia)
                        _viewModel.PreviousTheaterMediaCommand.Execute(null);
                    else if (_viewModel.TheaterPost?.IsPartOfThread == true)
                        _viewModel.SelectPreviousLinkedPartCommand.Execute(null);
                }
                _swipeAccumulator = 0;
                _lastSwipeTime = DateTime.Now;
            }
        }
    }

    private void TheaterMediaStage_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
    {
        _manipulationTranslationX += e.DeltaManipulation.Translation.X;
        e.Handled = true;
    }

    private void TheaterMediaStage_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
    {
        if (Math.Abs(_manipulationTranslationX) > 40)
        {
            if (_manipulationTranslationX < 0)
            {
                // Touch/gesture swiped left -> Next Image
                if (_viewModel.IsTheaterMediaVideo)
                    HandleTouchpadHorizontalScroll(120);
                else if (_viewModel.HasMultipleTheaterMedia)
                    _viewModel.NextTheaterMediaCommand.Execute(null);
                else if (_viewModel.TheaterPost?.IsPartOfThread == true)
                    _viewModel.SelectNextLinkedPartCommand.Execute(null);
            }
            else
            {
                // Touch/gesture swiped right -> Previous Image
                if (_viewModel.IsTheaterMediaVideo)
                    HandleTouchpadHorizontalScroll(-120);
                else if (_viewModel.HasMultipleTheaterMedia)
                    _viewModel.PreviousTheaterMediaCommand.Execute(null);
                else if (_viewModel.TheaterPost?.IsPartOfThread == true)
                    _viewModel.SelectPreviousLinkedPartCommand.Execute(null);
            }
        }
        _manipulationTranslationX = 0;
        e.Handled = true;
    }

    private void TheaterMediaStage_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel.IsTheaterMediaVideo)
        {
            // Touchpad 2-finger vertical scroll on video: adjust volume smoothly
            double step = e.Delta > 0 ? 0.05 : -0.05;
            VideoVolumeSlider.Value = Math.Clamp(VideoVolumeSlider.Value + step, 0.0, 1.0);
            e.Handled = true;
        }
    }

    private void TheaterVideoPlayer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double step = e.Delta > 0 ? 0.05 : -0.05;
        VideoVolumeSlider.Value = Math.Clamp(VideoVolumeSlider.Value + step, 0.0, 1.0);
        e.Handled = true;
    }

    private void TheaterVideoPlayer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                TheaterVideoPlayer.Stretch = TheaterVideoPlayer.Stretch == Stretch.Uniform ? Stretch.UniformToFill : Stretch.Uniform;
            }
            else
            {
                BtnPlayPause_Click(sender, e);
            }
            e.Handled = true;
        }
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

                string imgPath = SaveBitmapToStaging(bmp, "CanvasStaging");
                InsertMediaIntoFlowDocument(imgPath);
                return;
            }

            // 3. Check if HTML from web/forum with embedded images was pasted
            if (HtmlClipboardHelper.HasHtml(e.DataObject))
            {
                string? html = HtmlClipboardHelper.GetHtml(e.DataObject);
                if (!string.IsNullOrWhiteSpace(html))
                {
                    var imgSources = HtmlClipboardHelper.ExtractImageSources(html);
                    if (imgSources.Count > 0)
                    {
                        e.CancelCommand();
                        e.Handled = true;

                        string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        string stagingDir = Path.Combine(userDocs, "LocalSocialArchive", "Media", "CanvasStaging");
                        Directory.CreateDirectory(stagingDir);

                        string text = HtmlClipboardHelper.ConvertHtmlToFormattedText(html);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var para = new Paragraph(new Run(text))
                            {
                                FontSize = 14.5,
                                LineHeight = 22,
                                Margin = new Thickness(0, 0, 0, 12)
                            };
                            var caret = DocRichTextBox.CaretPosition;
                            var curPara = caret?.Paragraph;
                            if (curPara != null)
                            {
                                DocRichTextBox.Document.Blocks.InsertAfter(curPara, para);
                            }
                            else
                            {
                                DocRichTextBox.Document.Blocks.Add(para);
                            }
                            DocRichTextBox.CaretPosition = para.ContentEnd;
                        }

                        _ = Task.Run(async () =>
                        {
                            var localImages = await HtmlClipboardHelper.DownloadHtmlImagesAsync(html, stagingDir);
                            Dispatcher.Invoke(() =>
                            {
                                foreach (var img in localImages)
                                {
                                    InsertMediaIntoFlowDocument(img);
                                }
                            });
                        });

                        return;
                    }
                }
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
                string imgPath = SaveBitmapToStaging(bmp, "CanvasStaging");
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

    private static string SaveBitmapToStaging(BitmapSource bmp, string subfolder, string prefix = "pasted_img")
    {
        string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string stagingDir = Path.Combine(userDocs, "LocalSocialArchive", "Media", subfolder);
        Directory.CreateDirectory(stagingDir);
        string imgPath = Path.Combine(stagingDir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(imgPath))
        {
            encoder.Save(fs);
        }
        return imgPath;
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
        var doc = DocRichTextBox.Document;
        var caret = DocRichTextBox.CaretPosition;
        Paragraph? curPara = caret?.Paragraph;

        if (curPara != null)
        {
            var rangeAfter = new TextRange(caret, curPara.ContentEnd);
            string textAfter = rangeAfter.Text;

            if (!string.IsNullOrWhiteSpace(textAfter))
            {
                rangeAfter.Text = string.Empty;
                var nextPara = new Paragraph(new Run(textAfter.TrimStart('\r', '\n')))
                {
                    FontSize = curPara.FontSize,
                    Foreground = curPara.Foreground,
                    LineHeight = curPara.LineHeight,
                    Margin = curPara.Margin
                };

                doc.Blocks.InsertAfter(curPara, container);
                doc.Blocks.InsertAfter(container, nextPara);
                DocRichTextBox.CaretPosition = nextPara.ContentStart;
            }
            else
            {
                doc.Blocks.InsertAfter(curPara, container);
                var nextPara = new Paragraph
                {
                    FontSize = curPara.FontSize,
                    Foreground = curPara.Foreground,
                    LineHeight = curPara.LineHeight,
                    Margin = curPara.Margin
                };
                doc.Blocks.InsertAfter(container, nextPara);
                DocRichTextBox.CaretPosition = nextPara.ContentStart;
            }
        }
        else
        {
            Block? curBlock = doc.Blocks.FirstOrDefault(b => b.ContentStart.CompareTo(caret) <= 0 && b.ContentEnd.CompareTo(caret) >= 0);
            if (curBlock != null)
            {
                doc.Blocks.InsertAfter(curBlock, container);
                var nextPara = new Paragraph();
                doc.Blocks.InsertAfter(container, nextPara);
                DocRichTextBox.CaretPosition = nextPara.ContentStart;
            }
            else
            {
                doc.Blocks.Add(container);
                var nextPara = new Paragraph();
                doc.Blocks.Add(nextPara);
                DocRichTextBox.CaretPosition = nextPara.ContentStart;
            }
        }

        DocRichTextBox.Focus();
    }

    private void NodeTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is ThreadNode node)
            {
                var mediaFiles = ClipboardHelper.GetMediaFilesFromClipboard(e.DataObject);
                if (mediaFiles.Count > 0)
                {
                    e.CancelCommand();
                    e.Handled = true;
                    foreach (var f in mediaFiles)
                    {
                        if (!node.AttachedImagePaths.Contains(f))
                        {
                            node.AttachedImagePaths.Add(f);
                        }
                    }
                    return;
                }

                var bmp = ClipboardHelper.GetImageFromClipboard(e.DataObject);
                if (bmp != null)
                {
                    e.CancelCommand();
                    e.Handled = true;
                    string imgPath = SaveBitmapToStaging(bmp, "ThreadStaging", "thread_pasted");

                    if (!node.AttachedImagePaths.Contains(imgPath))
                    {
                        node.AttachedImagePaths.Add(imgPath);
                    }
                    return;
                }
            }
        }
        catch { }
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

    // ========== DRAG AND DROP POSTS TO FOLDERS ==========
    private Point _dragStartPoint;
    private bool _isDraggingPost;

    private void FeedList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
    }

    private void FeedList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDraggingPost) return;

        Point currentPoint = e.GetPosition(this);
        Vector diff = _dragStartPoint - currentPoint;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                var lvi = FindVisualAncestor<ListViewItem>(dep);
                if (lvi?.DataContext is Post post)
                {
                    _isDraggingPost = true;
                    try
                    {
                        var postsToMove = _viewModel.SelectedPosts.Count > 1 && _viewModel.SelectedPosts.Contains(post)
                            ? _viewModel.SelectedPosts.ToList()
                            : new List<Post> { post };

                        var data = new DataObject("LocalSocialArchive.Posts", postsToMove);
                        DragDrop.DoDragDrop(lvi, data, DragDropEffects.Move);
                    }
                    finally
                    {
                        _isDraggingPost = false;
                    }
                }
            }
        }
    }

    private void Folder_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("LocalSocialArchive.Posts"))
        {
            e.Effects = DragDropEffects.Move;
            if (sender is Border b)
            {
                b.BorderBrush = (Brush)FindResource("AccentBrush");
            }
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Folder_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b)
        {
            b.ClearValue(Border.BorderBrushProperty);
        }
    }

    private async void Folder_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border b)
        {
            b.ClearValue(Border.BorderBrushProperty);
        }

        if (e.Data.GetDataPresent("LocalSocialArchive.Posts"))
        {
            var posts = e.Data.GetData("LocalSocialArchive.Posts") as List<Post>;
            if (posts != null && posts.Count > 0)
            {
                string? targetFolder = null;
                if (sender is FrameworkElement fe)
                {
                    if (fe.DataContext is FolderCategory folderItem)
                        targetFolder = folderItem.Name;
                    else if (fe.DataContext is string folderName)
                        targetFolder = folderName;
                }

                if (!string.IsNullOrEmpty(targetFolder) && targetFolder != "All Posts")
                {
                    if (posts.Count > 1)
                    {
                        await _viewModel.MoveSelectedPostsToFolderAsync(targetFolder);
                    }
                    else
                    {
                        await _viewModel.MovePostToFolderAsync(new object[] { posts[0], targetFolder });
                    }
                }
            }
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}

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

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        Loaded += async (s, e) =>
        {
            StoragePathHelper.LogDiagnostic("MainWindow Loaded event fired. Initializing ViewModel...");
            try
            {
                await _viewModel.InitializeAsync();
                StoragePathHelper.LogDiagnostic("ViewModel initialization completed.");
            }
            catch (Exception ex)
            {
                StoragePathHelper.LogDiagnostic($"[MainWindow.Loaded Error] {ex}");
            }

            try
            {
                var source = PresentationSource.FromVisual(this) as HwndSource;
                source?.AddHook(WndProc);
            }
            catch (Exception ex)
            {
                StoragePathHelper.LogDiagnostic($"[WndProc Hook Notice] {ex.Message}");
            }
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
            else if (e.PropertyName == nameof(MainViewModel.SelectedThemeName))
            {
                ApplyTheme(_viewModel.SelectedThemeName);
            }
        };

        ApplyTheme(_viewModel.SelectedThemeName);
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
    // ========== GLOBAL KEYBOARD ACCELERATOR MANAGEMENT (PREVENTS INPUT HIJACK) ==========
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+K to jump to search bar instantly
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)
        {
            SearchInputBox.Focus();
            SearchInputBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && SearchInputBox.IsFocused)
        {
            _viewModel.ClearSearch();
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        // When focused on an active text input control (e.g. search box, tags box, comments, etc.)
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox)
        {
            // Allow native editing keys to pass through untouched
            if (e.Key is Key.Delete or Key.Back or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
            {
                return;
            }

            // Allow native clipboard operations to pass through untouched (Ctrl+V, Ctrl+C, Ctrl+X, Ctrl+A, Ctrl+Z, Ctrl+Y)
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.V or Key.C or Key.X or Key.A or Key.Z or Key.Y)
            {
                return;
            }
        }
        else
        {
            // Global Feed Shortcuts (only active when not typing inside a text box)
            if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_viewModel.SelectedPost != null || _viewModel.HasMultipleSelection)
                {
                    _viewModel.DeletePostCommand.Execute(_viewModel.SelectedPost);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _viewModel.PasteFromClipboardCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.T && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                _viewModel.OpenStoryCanvasCommand.Execute(null);
                e.Handled = true;
            }
            else if (_viewModel.IsTheaterModeOpen)
            {
                if (e.Key == Key.Left)
                {
                    _viewModel.PreviousTheaterMediaCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Right)
                {
                    _viewModel.NextTheaterMediaCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }

    // ========== DRAG AND DROP POSTS TO FOLDERS & RIGHT-CLICK SELECTION ==========
    private Point _dragStartPoint;
    private bool _isDraggingPost;

    private void FeedList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
    }

    private void FeedList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var lvi = FindVisualAncestor<ListViewItem>(dep);
            if (lvi?.DataContext is Post post)
            {
                // If not already in multi-selection, select this post exclusively
                if (!_viewModel.SelectedPosts.Contains(post))
                {
                    _viewModel.SelectedPost = post;
                    _viewModel.SelectedPosts.Clear();
                    _viewModel.SelectedPosts.Add(post);
                    lvi.IsSelected = true;
                    lvi.Focus();
                }
            }
        }
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
                        // Package multiple selected posts, or if part of a thread, package the whole thread
                        var postsToMove = _viewModel.SelectedPosts.Count > 1 && _viewModel.SelectedPosts.Contains(post)
                            ? _viewModel.SelectedPosts.ToList()
                            : (post.IsPartOfThread && !string.IsNullOrEmpty(post.ThreadId)
                                ? _viewModel.Posts.Where(p => p.ThreadId == post.ThreadId).ToList()
                                : new List<Post> { post });

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
                        // Check if these posts comprise an entire thread
                        if (posts.All(p => p.IsPartOfThread && p.ThreadId == posts[0].ThreadId))
                        {
                            await _viewModel.MoveThreadToFolderAsync(new object[] { posts[0], targetFolder });
                        }
                        else
                        {
                            await _viewModel.MoveSelectedPostsToFolderAsync(targetFolder);
                        }
                    }
                    else
                    {
                        if (posts[0].IsPartOfThread)
                        {
                            await _viewModel.MoveThreadToFolderAsync(new object[] { posts[0], targetFolder });
                        }
                        else
                        {
                            await _viewModel.MovePostToFolderAsync(new object[] { posts[0], targetFolder });
                        }
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

    // ========== CONTEXT MENU & FOLDER DROPDOWN DYNAMIC POPULATION & HANDLERS ==========
    private Post? GetPostFromMenuItem(object sender)
    {
        if (sender is MenuItem mi)
        {
            var cm = mi.Parent as ContextMenu ?? FindVisualAncestor<ContextMenu>(mi);
            if (cm?.PlacementTarget is FrameworkElement fe && fe.DataContext is Post p)
                return p;
        }
        return _viewModel.SelectedPost;
    }

    private void CardContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu cm) return;

        Post? post = null;
        if (cm.PlacementTarget is FrameworkElement fe && fe.DataContext is Post p)
        {
            post = p;
        }
        if (post == null) post = _viewModel.SelectedPost;
        if (post == null) return;

        foreach (var item in cm.Items)
        {
            if (item is MenuItem mi)
            {
                if (Equals(mi.Tag, "MovePost"))
                {
                    PopulateFolderSubmenu(mi, async folderName =>
                    {
                        await _viewModel.MovePostToFolderAsync(new object[] { post, folderName });
                    });
                }
                else if (Equals(mi.Tag, "MoveThread"))
                {
                    if (post.IsPartOfThread && !string.IsNullOrEmpty(post.ThreadId))
                    {
                        mi.Visibility = Visibility.Visible;
                        PopulateFolderSubmenu(mi, async folderName =>
                        {
                            await _viewModel.MoveThreadToFolderAsync(new object[] { post, folderName });
                        });
                    }
                    else
                    {
                        mi.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }
    }

    private void PopulateFolderSubmenu(MenuItem root, Func<string, Task> onFolderSelected)
    {
        root.Items.Clear();

        // 1. "Create New Folder..." option at top
        var createNewItem = new MenuItem
        {
            Header = "➕  Create New Folder...",
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x38)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            FontSize = 12.5,
            Padding = new Thickness(12, 7, 12, 7)
        };
        createNewItem.Click += async (s, args) =>
        {
            string? newFolderName = _viewModel.PromptCreateFolder();
            if (!string.IsNullOrWhiteSpace(newFolderName))
            {
                await onFolderSelected(newFolderName);
            }
        };
        root.Items.Add(createNewItem);

        root.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x33, 0x36)), Margin = new Thickness(0, 2, 0, 2) });

        // 2. Existing destination folders
        foreach (var folder in _viewModel.DestinationFolders)
        {
            var mi = new MenuItem
            {
                Header = $"📁  {folder.Name}",
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0xE9, 0xEA)),
                FontSize = 12.5,
                Padding = new Thickness(12, 6, 12, 6)
            };
            string folderName = folder.Name;
            mi.Click += async (s, args) =>
            {
                await onFolderSelected(folderName);
            };
            root.Items.Add(mi);
        }
    }

    private void MoveSelectedMenu_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is MenuItem root && !root.IsSubmenuOpen)
        {
            PopulateFolderSubmenu(root, async folderName =>
            {
                await _viewModel.MoveSelectedPostsToFolderAsync(folderName);
            });
            root.IsSubmenuOpen = true;
            e.Handled = true;
        }
    }

    private void MoveSelectedMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem root)
        {
            PopulateFolderSubmenu(root, async folderName =>
            {
                await _viewModel.MoveSelectedPostsToFolderAsync(folderName);
            });
        }
    }

    private void InspectorMovePost_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is MenuItem root && !root.IsSubmenuOpen)
        {
            PopulateFolderSubmenu(root, async folderName =>
            {
                await _viewModel.MovePostToFolderAsync(folderName);
            });
            root.IsSubmenuOpen = true;
            e.Handled = true;
        }
    }

    private void InspectorMovePost_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem root)
        {
            PopulateFolderSubmenu(root, async folderName =>
            {
                await _viewModel.MovePostToFolderAsync(folderName);
            });
        }
    }

    private void InspectorMoveThread_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is MenuItem root && !root.IsSubmenuOpen)
        {
            PopulateFolderSubmenu(root, async folderName =>
            {
                await _viewModel.MoveThreadToFolderAsync(folderName);
            });
            root.IsSubmenuOpen = true;
            e.Handled = true;
        }
    }

    private void InspectorMoveThread_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem root)
        {
            PopulateFolderSubmenu(root, async folderName =>
            {
                await _viewModel.MoveThreadToFolderAsync(folderName);
            });
        }
    }

    private void ContextOpenTheater_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenTheaterMode(GetPostFromMenuItem(sender));
    }

    private async void ContextEditPost_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.EditPostAsync(GetPostFromMenuItem(sender));
    }

    private void ContextOpenThreadReader_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenThreadReader(GetPostFromMenuItem(sender));
    }

    private void ContextViewFullStory_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ViewFullStory(GetPostFromMenuItem(sender));
    }

    private async void ContextUnlinkStory_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.UnlinkFromStoryAsync(GetPostFromMenuItem(sender));
    }

    private async void ContextLinkStory_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LinkSelectedPostsAsStoryAsync();
    }

    private void ContextOpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenLocalFolder(GetPostFromMenuItem(sender));
    }

    private void ContextCopyTweet_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CopyPostText(GetPostFromMenuItem(sender));
    }

    private async void ContextDeletePost_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.DeletePostAsync(GetPostFromMenuItem(sender));
    }

    private async void ContextDeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.DeleteSelectedPostsAsync();
    }

    private async void ContextPasteComments_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.OpenPasteCommentsWindowAsync(GetPostFromMenuItem(sender));
    }

    private async void ContextQuickPasteComments_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.QuickPasteClipboardCommentsAsync(GetPostFromMenuItem(sender));
    }

    private async void InlineCommentBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            var text = tb.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            var targetPost = tb.Tag as Post ?? _viewModel.SelectedPost;
            if (targetPost != null)
            {
                tb.Text = string.Empty;
                await _viewModel.AddInlineCommentAsync(new object[] { targetPost, text });
            }
        }
    }

    private async void PostInlineComment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            var parentGrid = fe.Parent as Grid ?? FindVisualAncestor<Grid>(fe);
            if (parentGrid != null)
            {
                TextBox? tb = null;
                foreach (var child in parentGrid.Children)
                {
                    if (child is TextBox t)
                    {
                        tb = t;
                        break;
                    }
                }

                if (tb != null)
                {
                    var text = tb.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var targetPost = tb.Tag as Post ?? fe.DataContext as Post ?? _viewModel.SelectedPost;
                        if (targetPost != null)
                        {
                            tb.Text = string.Empty;
                            await _viewModel.AddInlineCommentAsync(new object[] { targetPost, text });
                        }
                    }
                }
            }
        }
    }

    public void ApplyTheme(string? themeName)
    {
        string bgHex, sidebarHex, cardHex, borderHex, accentHex, accentHoverHex, accentBadgeHex;

        switch (themeName)
        {
            case "Deep Slate":
                bgHex = "#0D1117";
                sidebarHex = "#161B22";
                cardHex = "#1C2128";
                borderHex = "#30363D";
                accentHex = "#1D9BF0";
                accentHoverHex = "#38BDF8";
                accentBadgeHex = "#1A2F45";
                break;
            case "OLED Black":
                bgHex = "#000000";
                sidebarHex = "#0A0A0A";
                cardHex = "#141414";
                borderHex = "#262626";
                accentHex = "#E7E9EA";
                accentHoverHex = "#FFFFFF";
                accentBadgeHex = "#222222";
                break;
            case "Azure Glow":
                bgHex = "#0A131F";
                sidebarHex = "#0E1B2B";
                cardHex = "#132438";
                borderHex = "#1E3550";
                accentHex = "#38BDF8";
                accentHoverHex = "#60CDFF";
                accentBadgeHex = "#0F3354";
                break;
            case "Emerald Forest":
                bgHex = "#091612";
                sidebarHex = "#0E1E19";
                cardHex = "#132B23";
                borderHex = "#1C3F34";
                accentHex = "#10B981";
                accentHoverHex = "#34D399";
                accentBadgeHex = "#0D382B";
                break;
            case "Amethyst Violet":
                bgHex = "#130C1C";
                sidebarHex = "#191124";
                cardHex = "#221730";
                borderHex = "#35244C";
                accentHex = "#A855F7";
                accentHoverHex = "#C084FC";
                accentBadgeHex = "#341952";
                break;
            case "Windows Dark":
            default:
                bgHex = "#1C2028";
                sidebarHex = "#181C23";
                cardHex = "#202530";
                borderHex = "#2F3644";
                accentHex = "#4CC2FF";
                accentHoverHex = "#38BDF8";
                accentBadgeHex = "#1A2D42";
                break;
        }

        var bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex));
        var sidebarBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sidebarHex));
        var cardBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cardHex));
        var borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderHex));
        var accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentHex));
        var accentHoverBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentHoverHex));
        var accentBadgeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentBadgeHex));

        bgBrush.Freeze();
        sidebarBrush.Freeze();
        cardBrush.Freeze();
        borderBrush.Freeze();
        accentBrush.Freeze();
        accentHoverBrush.Freeze();
        accentBadgeBrush.Freeze();

        Resources["BgDarkBrush"] = bgBrush;
        Resources["SidebarBgBrush"] = sidebarBrush;
        Resources["RailBgBrush"] = sidebarBrush;
        Resources["CardBgBrush"] = cardBrush;
        Resources["CardBorderBrush"] = borderBrush;
        Resources["AccentBrush"] = accentBrush;
        Resources["AccentHoverBrush"] = accentHoverBrush;
        Resources["AccentBadgeBgBrush"] = accentBadgeBrush;
        Resources["InspectorBgBrush"] = sidebarBrush;

        if (Application.Current != null)
        {
            Application.Current.Resources["AppBgBrush"] = bgBrush;
            Application.Current.Resources["CardBgBrush"] = cardBrush;
            Application.Current.Resources["CardBorderBrush"] = borderBrush;
            Application.Current.Resources["AccentBrush"] = accentBrush;
        }
    }
}

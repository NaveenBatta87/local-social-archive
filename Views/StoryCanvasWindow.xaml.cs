using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalSocialArchive.Helpers;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class StoryCanvasWindow : Window
{
    private StoryCanvasViewModel _viewModel;
    private bool _isUpdatingInternally;
    private readonly System.Windows.Threading.DispatcherTimer _sizeDebounceTimer;

    public StoryCanvasWindow(StoryCanvasViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.RequestClose += () => Close();

        // Adaptively size and center to guarantee perfect placement on laptops of any resolution / scaling
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1140, Math.Max(MinWidth, workArea.Width * 0.90));
        Height = Math.Min(780, Math.Max(MinHeight, workArea.Height * 0.88));
        Left = workArea.Left + (workArea.Width - Width) / 2.0;
        Top = workArea.Top + (workArea.Height - Height) / 2.0;

        _sizeDebounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _sizeDebounceTimer.Tick += (s, e) =>
        {
            _sizeDebounceTimer.Stop();
            _viewModel.RecalculateSize();
        };

        DataObject.AddPastingHandler(WordRichTextBox, async (s, e) =>
        {
            e.CancelCommand();
            SyncRichTextBoxToCurrentPart();
            await _viewModel.PasteFromClipboardAsync();
            LoadCurrentPartIntoRichTextBox();
        });

        PreviewKeyDown += async (s, e) =>
        {
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                SyncRichTextBoxToCurrentPart();
                await _viewModel.SaveStoryDocumentAsync();
            }
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadCurrentPartIntoRichTextBox();
    }

    private void LoadCurrentPartIntoRichTextBox()
    {
        if (_viewModel.CurrentPart == null) return;
        _isUpdatingInternally = true;
        try
        {
            var flowDoc = CanvasDocumentHelper.CreateFlowDocument(_viewModel.CurrentPart.Blocks, 15.5);
            WordRichTextBox.Document = flowDoc;
            _viewModel.RecalculateSize();
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    private void SyncRichTextBoxToCurrentPart()
    {
        if (_viewModel.CurrentPart == null || WordRichTextBox.Document == null) return;
        var blocks = CanvasDocumentHelper.ExtractBlocksFromFlowDocument(WordRichTextBox.Document);
        _viewModel.CurrentPart.Blocks.Clear();
        foreach (var b in blocks)
        {
            _viewModel.CurrentPart.Blocks.Add(b);
        }
        _viewModel.RecalculateSize();
    }

    private async void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        SyncRichTextBoxToCurrentPart();
        await _viewModel.PasteFromClipboardAsync();
        LoadCurrentPartIntoRichTextBox();
    }

    private void CleanTextStylingButton_Click(object sender, RoutedEventArgs e)
    {
        if (WordRichTextBox.Document == null) return;
        CanvasDocumentHelper.CleanFlowDocumentTextStyling(WordRichTextBox.Document);
        SyncRichTextBoxToCurrentPart();
        _viewModel.StatusMessage = "🧹 Cleaned text styling. Web fonts & colors stripped; inline images preserved.";
    }

    private void SplitPartButton_Click(object sender, RoutedEventArgs e)
    {
        SyncRichTextBoxToCurrentPart();
        _viewModel.SplitIntoNewPart();
        LoadCurrentPartIntoRichTextBox();
    }

    private void PreviousPartButton_Click(object sender, RoutedEventArgs e)
    {
        SyncRichTextBoxToCurrentPart();
        _viewModel.PreviousPart();
        LoadCurrentPartIntoRichTextBox();
        CanvasScrollViewer.ScrollToTop();
    }

    private void NextPartButton_Click(object sender, RoutedEventArgs e)
    {
        SyncRichTextBoxToCurrentPart();
        _viewModel.NextPart();
        LoadCurrentPartIntoRichTextBox();
        CanvasScrollViewer.ScrollToTop();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SyncRichTextBoxToCurrentPart();
        await _viewModel.SaveStoryDocumentAsync();
    }

    private void OpenReaderButton_Click(object sender, RoutedEventArgs e)
    {
        SyncRichTextBoxToCurrentPart();
        _viewModel.OpenReader();
    }

    private void WordRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInternally) return;
        // Debounce size refresh to eliminate typing jitter and UI thread stalls
        _sizeDebounceTimer.Stop();
        _sizeDebounceTimer.Start();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
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
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

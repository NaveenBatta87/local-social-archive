using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class NewTweetWindow : Window
{
    private readonly NewTweetViewModel _viewModel;

    public NewTweetWindow(NewTweetViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.RequestClose += (result) =>
        {
            DialogResult = result;
            Close();
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        TxtContent.Focus();
        TxtContent.CaretIndex = TxtContent.Text.Length;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl + Enter -> Quick Save / Archive
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            _viewModel.SaveCommand.Execute(null);
            return;
        }

        // Escape -> Close
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
            Close();
            return;
        }

        // Ctrl + V -> Check if clipboard has an image or file list to attach
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (TryPasteMediaFromClipboard())
            {
                e.Handled = true;
                return;
            }
        }
    }

    private bool TryPasteMediaFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                var list = new List<string>();
                foreach (string? f in files)
                {
                    if (!string.IsNullOrWhiteSpace(f)) list.Add(f);
                }
                if (list.Count > 0)
                {
                    _viewModel.AddDroppedFiles(list);
                    return true;
                }
            }

            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    string tempPath = Path.Combine(Path.GetTempPath(), $"pasted_img_{Guid.NewGuid():N}.png");
                    using (var fileStream = new FileStream(tempPath, FileMode.Create))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        encoder.Save(fileStream);
                    }
                    _viewModel.AttachedImagePaths.Add(tempPath);
                    return true;
                }
            }
        }
        catch
        {
            // Fall back to normal text paste
        }

        return false;
    }

    private void Content_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Content_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                _viewModel.AddDroppedFiles(files);
                e.Handled = true;
            }
        }
    }

    private void BtnWindowsEmojiPicker_Click(object sender, RoutedEventArgs e)
    {
        TxtContent.Focus();
        Helpers.EmojiPacks.OpenWindowsEmojiPicker();
    }
}

using System.Windows;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class EditPostWindow : Window
{
    public EditPostWindow(EditPostViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += (result) =>
        {
            DialogResult = result;
            Close();
        };
    }

    private void BtnWindowsEmojiPicker_Click(object sender, RoutedEventArgs e)
    {
        TxtContent.Focus();
        Helpers.EmojiPacks.OpenWindowsEmojiPicker();
    }
}

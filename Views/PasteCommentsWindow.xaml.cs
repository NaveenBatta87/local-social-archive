using System.Windows;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class PasteCommentsWindow : Window
{
    public PasteCommentsWindow(PasteCommentsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += (result) =>
        {
            DialogResult = result;
            Close();
        };
    }
}

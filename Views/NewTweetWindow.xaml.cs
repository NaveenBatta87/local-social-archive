using System.Windows;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class NewTweetWindow : Window
{
    public NewTweetWindow(NewTweetViewModel viewModel)
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

using System.Windows;
using LocalSocialArchive.ViewModels;

namespace LocalSocialArchive.Views;

public partial class AddCollectionWindow : Window
{
    public AddCollectionWindow(AddCollectionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

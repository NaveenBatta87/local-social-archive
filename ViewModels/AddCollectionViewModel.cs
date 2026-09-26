using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LocalSocialArchive.ViewModels;

public partial class AddCollectionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _collectionName = string.Empty;

    [ObservableProperty]
    private string _description = "Collection of archived folders";

    [ObservableProperty]
    private string _selectedIcon = "🗂️";

    [ObservableProperty]
    private string _selectedColor = "#1D9BF0";

    public ObservableCollection<string> AvailableIcons { get; } = new()
    {
        "🗂️", "💻", "💼", "🌟", "📚", "🔬", "🎨", "🚀", "⚡", "🔒", "💡", "🎮"
    };

    public ObservableCollection<string> AvailableColors { get; } = new()
    {
        "#1D9BF0", "#10B981", "#8B5CF6", "#F59E0B", "#EC4899", "#38BDF8", "#6366F1", "#14B8A6"
    };

    public bool? DialogResult { get; private set; }

    [RelayCommand]
    private void Save(Window? window)
    {
        if (string.IsNullOrWhiteSpace(CollectionName))
        {
            MessageBox.Show("Please enter a collection name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        if (window != null)
        {
            window.DialogResult = true;
            window.Close();
        }
    }

    [RelayCommand]
    private void Cancel(Window? window)
    {
        DialogResult = false;
        if (window != null)
        {
            window.DialogResult = false;
            window.Close();
        }
    }
}

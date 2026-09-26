using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LocalSocialArchive.ViewModels;

public partial class AddFolderViewModel : ObservableObject
{
    [ObservableProperty]
    private string _folderName = string.Empty;

    [ObservableProperty]
    private string _collectionName = "Engineering & Tech";

    [ObservableProperty]
    private string _description = "Offline categorized archive";

    [ObservableProperty]
    private string _selectedIcon = "📁";

    [ObservableProperty]
    private ObservableCollection<string> _availableCollections = new();

    public ObservableCollection<string> AvailableIcons { get; } = new()
    {
        "📁", "💻", "💼", "🎭", "📚", "⚡", "📑", "💡", "🛠️", "🚀", "🎯", "🏷️"
    };

    public event Action<bool>? RequestClose;

    public AddFolderViewModel()
    {
    }

    public AddFolderViewModel(IEnumerable<string> collections, string? defaultCollection = null)
    {
        AvailableCollections = new ObservableCollection<string>(collections);
        if (!string.IsNullOrWhiteSpace(defaultCollection) && AvailableCollections.Contains(defaultCollection))
        {
            CollectionName = defaultCollection;
        }
        else if (AvailableCollections.Count > 0)
        {
            CollectionName = AvailableCollections[0];
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(FolderName))
        {
            MessageBox.Show("Please enter a folder name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(CollectionName))
        {
            CollectionName = "General Archive";
        }

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}

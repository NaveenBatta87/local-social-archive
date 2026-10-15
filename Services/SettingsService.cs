using System.IO;
using System.Text.Json;

namespace LocalSocialArchive.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;

    public string DefaultFolder { get; set; } = "Coding";
    public bool AutoExtractHashtags { get; set; } = true;
    public bool IsGridView { get; set; } = false;
    public bool IsInspectorVisible { get; set; } = true;
    public bool IsTagsVisible { get; set; } = true;
    public bool IsSidebarExpanded { get; set; } = true;
    public double GridCardWidth { get; set; } = 320;
    public double AppScale { get; set; } = 1.25;
    public string ThemeName { get; set; } = "Windows Dark";

    public SettingsService()
    {
        _settingsFilePath = LocalSocialArchive.Helpers.StoragePathHelper.SettingsFilePath;
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                if (data != null)
                {
                    if (!string.IsNullOrWhiteSpace(data.DefaultFolder)) DefaultFolder = data.DefaultFolder;
                    AutoExtractHashtags = data.AutoExtractHashtags;
                    IsGridView = data.IsGridView;
                    IsInspectorVisible = data.IsInspectorVisible;
                    IsTagsVisible = data.IsTagsVisible;
                    IsSidebarExpanded = data.IsSidebarExpanded;
                    if (data.GridCardWidth >= 180 && data.GridCardWidth <= 600)
                    {
                        GridCardWidth = data.GridCardWidth;
                    }
                    if (data.AppScale >= 0.75 && data.AppScale <= 2.0)
                    {
                        AppScale = data.AppScale;
                    }
                    if (!string.IsNullOrWhiteSpace(data.ThemeName))
                    {
                        ThemeName = data.ThemeName;
                    }
                }
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            var data = new SettingsData
            {
                DefaultFolder = DefaultFolder,
                AutoExtractHashtags = AutoExtractHashtags,
                IsGridView = IsGridView,
                IsInspectorVisible = IsInspectorVisible,
                IsTagsVisible = IsTagsVisible,
                IsSidebarExpanded = IsSidebarExpanded,
                GridCardWidth = GridCardWidth,
                AppScale = AppScale,
                ThemeName = ThemeName
            };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch { }
    }

    private class SettingsData
    {
        public string DefaultFolder { get; set; } = "Coding";
        public bool AutoExtractHashtags { get; set; } = true;
        public bool IsGridView { get; set; } = false;
        public bool IsInspectorVisible { get; set; } = true;
        public bool IsTagsVisible { get; set; } = true;
        public bool IsSidebarExpanded { get; set; } = true;
        public double GridCardWidth { get; set; } = 320;
        public double AppScale { get; set; } = 1.25;
        public string ThemeName { get; set; } = "Windows Dark";
    }
}

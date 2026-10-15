namespace LocalSocialArchive.Services;

public interface ISettingsService
{
    string DefaultFolder { get; set; }
    bool AutoExtractHashtags { get; set; }
    bool IsGridView { get; set; }
    bool IsInspectorVisible { get; set; }
    bool IsTagsVisible { get; set; }
    bool IsSidebarExpanded { get; set; }
    double GridCardWidth { get; set; }
    double AppScale { get; set; }
    string ThemeName { get; set; }

    void Load();
    void Save();
}

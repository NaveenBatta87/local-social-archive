using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalSocialArchive.Models;

public enum CanvasBlockType
{
    Paragraph,
    Image
}

public partial class CanvasBlock : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private CanvasBlockType _type = CanvasBlockType.Paragraph;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string? _imagePath;

    [ObservableProperty]
    private long _imageSizeBytes;

    [ObservableProperty]
    private string? _caption;

    public bool IsParagraph => Type == CanvasBlockType.Paragraph;
    public bool IsImage => Type == CanvasBlockType.Image;
    public bool HasImageFile => IsImage && !string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath);

    public string ImageSizeFormatted
    {
        get
        {
            if (ImageSizeBytes <= 0) return string.Empty;
            if (ImageSizeBytes < 1024) return $"{ImageSizeBytes} B";
            if (ImageSizeBytes < 1024 * 1024) return $"{ImageSizeBytes / 1024.0:F1} KB";
            return $"{ImageSizeBytes / (1024.0 * 1024.0):F2} MB";
        }
    }
}

public partial class CanvasDocumentPart : ObservableObject
{
    [ObservableProperty]
    private string _partId = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private int _partIndex = 1;

    [ObservableProperty]
    private int _totalParts = 1;

    [ObservableProperty]
    private string _title = "Untitled Story";

    [ObservableProperty]
    private ObservableCollection<CanvasBlock> _blocks = new();

    public long TotalSizeBytes
    {
        get
        {
            long size = 0;
            foreach (var b in Blocks)
            {
                if (b.IsParagraph)
                {
                    size += Encoding.UTF8.GetByteCount(b.Text ?? string.Empty);
                }
                else if (b.IsImage && b.ImageSizeBytes > 0)
                {
                    size += b.ImageSizeBytes;
                }
            }
            return size;
        }
    }

    public double TotalSizeMB => TotalSizeBytes / (1024.0 * 1024.0);
    public bool Exceeds12MB => TotalSizeBytes > (12L * 1024 * 1024);

    public string SizeBadgeText => $"{TotalSizeMB:F2} MB / 12.0 MB";
}

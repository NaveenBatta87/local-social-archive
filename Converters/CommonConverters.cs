using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace LocalSocialArchive.Converters;

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility v)
        {
            return v != Visibility.Visible;
        }
        return false;
    }
}

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNullOrEmpty = value == null || (value is string s && string.IsNullOrWhiteSpace(s));
        if (Invert)
        {
            return isNullOrEmpty ? Visibility.Visible : Visibility.Collapsed;
        }
        return isNullOrEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class FileSizeFormatterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
        return "0 MB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToLockToggleTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? "Disable Startup Lock" : "Enable Startup Lock";
        }
        return "Toggle Lock";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class CodiconGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            return LocalSocialArchive.Helpers.Codicons.ToCodicon(s);
        }
        return LocalSocialArchive.Helpers.Codicons.Folder;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ThumbnailImageConverter : IValueConverter
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapImage> _cache = new();
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _keyQueue = new();
    private const int MaxCacheEntries = 450;
    private const int TrimBatch = 80;

    public int DecodeWidth { get; set; } = 480;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        if (!File.Exists(path))
            return null;

        int width = DecodeWidth;
        if (parameter is string pStr && int.TryParse(pStr, out int customWidth) && customWidth > 50)
        {
            width = customWidth;
        }

        string cacheKey = $"{path}_{width}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.DecodePixelWidth = width;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.DelayCreation;
            bitmap.EndInit();
            bitmap.Freeze();

            // Generational cache trimming: evict oldest batch without flushing entire cache
            if (_cache.Count > MaxCacheEntries)
            {
                for (int i = 0; i < TrimBatch && _keyQueue.TryDequeue(out var oldKey); i++)
                {
                    _cache.TryRemove(oldKey, out _);
                }
            }

            _cache[cacheKey] = bitmap;
            _keyQueue.Enqueue(cacheKey);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class HexToBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.BrushConverter _converter = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var brush = (System.Windows.Media.Brush?)_converter.ConvertFromString(hex);
                if (brush != null)
                {
                    brush.Freeze();
                    return brush;
                }
            }
            catch { }
        }
        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}


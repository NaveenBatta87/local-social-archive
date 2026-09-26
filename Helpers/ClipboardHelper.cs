using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LocalSocialArchive.Helpers;

public static class ClipboardHelper
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".jif", ".webp", ".bmp", ".gif"
    };

    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".mkv", ".avi", ".wmv", ".m4v", ".flv"
    };

    public static bool IsImageExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return SupportedImageExtensions.Contains(ext);
    }

    public static bool IsVideoExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return SupportedVideoExtensions.Contains(ext);
    }

    public static bool IsMediaExtension(string? path)
    {
        return IsImageExtension(path) || IsVideoExtension(path);
    }

    public static BitmapSource? GetImageFromClipboard(IDataObject? dataObject = null)
    {
        var data = dataObject ?? Clipboard.GetDataObject();
        if (data == null) return null;

        // 1. Direct WPF Image
        try
        {
            if (data.GetDataPresent(DataFormats.Bitmap))
            {
                var bmp = data.GetData(DataFormats.Bitmap) as BitmapSource;
                if (bmp != null) return bmp;
            }
        }
        catch { }

        // 2. PNG Stream (Common from browsers like Chrome / Edge)
        try
        {
            if (data.GetDataPresent("PNG"))
            {
                var pngStream = data.GetData("PNG") as Stream;
                if (pngStream != null)
                {
                    var decoder = new PngBitmapDecoder(pngStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count > 0) return decoder.Frames[0];
                }
            }
        }
        catch { }

        // 3. Device Independent Bitmap (CF_DIB / CF_DIBV5 from MS Word, Paint, etc.)
        try
        {
            if (data.GetDataPresent(DataFormats.Dib))
            {
                var dibStream = data.GetData(DataFormats.Dib) as MemoryStream;
                if (dibStream != null)
                {
                    var bmp = ConvertDibToBitmapSource(dibStream.ToArray());
                    if (bmp != null) return bmp;
                }
            }
        }
        catch { }

        // 4. File Drop List (if image files were copied)
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                if (data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    foreach (var file in files)
                    {
                        if (File.Exists(file) && IsImageExtension(file))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(file, UriKind.Absolute);
                            bitmap.EndInit();
                            bitmap.Freeze();
                            return bitmap;
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public static List<string> GetMediaFilesFromClipboard(IDataObject? dataObject = null)
    {
        var results = new List<string>();
        var data = dataObject ?? Clipboard.GetDataObject();
        if (data == null) return results;

        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                if (data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    foreach (var file in files)
                    {
                        if (File.Exists(file) && IsMediaExtension(file))
                        {
                            results.Add(file);
                        }
                    }
                }
            }
        }
        catch { }

        return results;
    }

    public static BitmapSource? ConvertDibToBitmapSource(byte[] dibBytes)
    {
        if (dibBytes == null || dibBytes.Length < 40) return null;

        try
        {
            // BITMAPINFOHEADER structure size is usually 40 (or BITMAPV5HEADER 124)
            int headerSize = BitConverter.ToInt32(dibBytes, 0);
            int width = BitConverter.ToInt32(dibBytes, 4);
            int height = BitConverter.ToInt32(dibBytes, 8);
            short planes = BitConverter.ToInt16(dibBytes, 12);
            short bitCount = BitConverter.ToInt16(dibBytes, 14);
            int compression = BitConverter.ToInt32(dibBytes, 16);
            int imageSize = BitConverter.ToInt32(dibBytes, 20);
            int colorsUsed = BitConverter.ToInt32(dibBytes, 32);

            // Construct standard BMP file header (14 bytes) + DIB bytes
            int fileHeaderSize = 14;
            int paletteSize = 0;
            if (bitCount <= 8)
            {
                paletteSize = (colorsUsed == 0 ? (1 << bitCount) : colorsUsed) * 4;
            }
            else if (compression == 3) // BI_BITFIELDS
            {
                paletteSize = 12; // 3 DWORD masks
            }

            int offBits = fileHeaderSize + headerSize + paletteSize;
            int totalFileSize = fileHeaderSize + dibBytes.Length;

            var bmpFileBytes = new byte[totalFileSize];
            // 'BM' identifier
            bmpFileBytes[0] = 0x42;
            bmpFileBytes[1] = 0x4D;
            // File size
            BitConverter.GetBytes(totalFileSize).CopyTo(bmpFileBytes, 2);
            // Reserved
            bmpFileBytes[6] = 0;
            bmpFileBytes[7] = 0;
            bmpFileBytes[8] = 0;
            bmpFileBytes[9] = 0;
            // Offset to pixel data
            BitConverter.GetBytes(offBits).CopyTo(bmpFileBytes, 10);
            // Copy DIB data
            Array.Copy(dibBytes, 0, bmpFileBytes, fileHeaderSize, dibBytes.Length);

            using var ms = new MemoryStream(bmpFileBytes);
            var decoder = new BmpBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count > 0)
            {
                return decoder.Frames[0];
            }
        }
        catch { }

        return null;
    }
}

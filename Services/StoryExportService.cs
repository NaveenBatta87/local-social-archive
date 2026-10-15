using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalSocialArchive.Models;
using LocalSocialArchive.Views;

namespace LocalSocialArchive.Services;

public class StoryExportService : IStoryExportService
{
    public async Task<StoryExportResult> ExportCardsAsync(
        IEnumerable<StoryCard> cards,
        string targetDirectory,
        string baseName,
        IProgress<(int current, int total)>? progress = null)
    {
        var cardList = cards.ToList();
        var result = new StoryExportResult
        {
            OutputDirectory = targetDirectory
        };

        if (cardList.Count == 0)
        {
            result.Success = false;
            result.ErrorMessage = "No story cards to export.";
            return result;
        }

        try
        {
            Directory.CreateDirectory(targetDirectory);
            string safeBaseName = SanitizeFileName(baseName);
            if (string.IsNullOrWhiteSpace(safeBaseName))
            {
                safeBaseName = "Story_Thread";
            }

            int total = cardList.Count;

            for (int i = 0; i < total; i++)
            {
                var card = cardList[i];
                int currentNumber = i + 1;

                string filePath = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Render card at exact 1080 x 1920 dimension
                    var view = new StoryCardCanvasView
                    {
                        DataContext = card,
                        Width = 1080,
                        Height = 1920
                    };

                    view.Measure(new Size(1080, 1920));
                    view.Arrange(new Rect(0, 0, 1080, 1920));
                    view.UpdateLayout();

                    var rtb = new RenderTargetBitmap(1080, 1920, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(view);

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(rtb));

                    string fileName = $"{safeBaseName}_card_{currentNumber:D2}_of_{total:D2}.png";
                    string destinationPath = Path.Combine(targetDirectory, fileName);

                    using (var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        encoder.Save(fs);
                    }

                    return destinationPath;
                });

                result.ExportedFiles.Add(filePath);
                progress?.Report((currentNumber, total));
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return clean.Trim().Replace(' ', '_');
    }
}

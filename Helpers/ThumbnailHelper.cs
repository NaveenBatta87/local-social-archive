using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalSocialArchive.Helpers;

public static class ThumbnailHelper
{
    public static async Task<string?> GenerateVideoThumbnailAsync(string videoPath, string outputThumbnailPath)
    {
        if (!File.Exists(videoPath)) return null;

        try
        {
            if (File.Exists(outputThumbnailPath))
            {
                return outputThumbnailPath;
            }

            var dir = Path.GetDirectoryName(outputThumbnailPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

        if (Application.Current?.Dispatcher == null) return null;

        var task = await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var mediaPlayer = new MediaPlayer
            {
                Volume = 0,
                ScrubbingEnabled = true
            };

            var tcs = new TaskCompletionSource<string?>();

            void CleanUp()
            {
                try
                {
                    mediaPlayer.Stop();
                    mediaPlayer.Close();
                }
                catch { }
            }

            mediaPlayer.MediaOpened += (s, e) =>
            {
                try
                {
                    var duration = mediaPlayer.NaturalDuration.HasTimeSpan ? mediaPlayer.NaturalDuration.TimeSpan : TimeSpan.FromSeconds(2);
                    var targetSeconds = Math.Min(1.0, duration.TotalSeconds > 0.5 ? duration.TotalSeconds / 2.0 : 0.1);
                    mediaPlayer.Position = TimeSpan.FromSeconds(targetSeconds);

                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(250)
                    };

                    timer.Tick += (st, et) =>
                    {
                        timer.Stop();
                        try
                        {
                            int width = mediaPlayer.NaturalVideoWidth > 0 ? mediaPlayer.NaturalVideoWidth : 640;
                            int height = mediaPlayer.NaturalVideoHeight > 0 ? mediaPlayer.NaturalVideoHeight : 360;

                            if (width > 800 || height > 800)
                            {
                                double scale = Math.Min(800.0 / width, 800.0 / height);
                                width = Math.Max(1, (int)(width * scale));
                                height = Math.Max(1, (int)(height * scale));
                            }

                            var drawingVisual = new DrawingVisual();
                            using (var dc = drawingVisual.RenderOpen())
                            {
                                dc.DrawVideo(mediaPlayer, new Rect(0, 0, width, height));
                            }

                            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                            rtb.Render(drawingVisual);

                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(rtb));

                            using (var fs = File.Create(outputThumbnailPath))
                            {
                                encoder.Save(fs);
                            }

                            CleanUp();
                            tcs.TrySetResult(outputThumbnailPath);
                        }
                        catch
                        {
                            CleanUp();
                            tcs.TrySetResult(null);
                        }
                    };
                    timer.Start();
                }
                catch
                {
                    CleanUp();
                    tcs.TrySetResult(null);
                }
            };

            mediaPlayer.MediaFailed += (s, e) =>
            {
                CleanUp();
                tcs.TrySetResult(null);
            };

            mediaPlayer.Open(new Uri(videoPath, UriKind.Absolute));
            mediaPlayer.Play();
            mediaPlayer.Pause();

            // 3-second timeout fallback
            var timeoutTask = Task.Delay(3000);
            var completed = await Task.WhenAny(tcs.Task, timeoutTask);
            if (completed == timeoutTask)
            {
                CleanUp();
                return null;
            }

            return await tcs.Task;
        });

        return await task;
        }
        catch
        {
            return null;
        }
    }
}

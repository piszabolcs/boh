using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BOH.Models;
using BOH.Services;
using BOH.ViewModels;
using BOH.Views;

namespace BOH.Tests;

/// <summary>
/// GUI smoke test: builds the real window with demo data and renders its content into a bitmap. The window is
/// never shown. With BOH_SCREENSHOT_DIR set, the renders are written there (the README screenshots in docs/).
/// </summary>
public class WindowRenderTests
{
    [Fact]
    public void MainWindow_RendersOffscreen_WithDemoData()
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RenderScreens();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static void RenderScreens()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        var app = new App();
        app.InitializeComponent(); // theme and styles, as in the real app

        using var tmp = new TempDir();
        var window = new MainWindow(DemoViewModel(tmp));
        var main = Render((FrameworkElement)window.Content, 1100, 780);
        Assert.True(DistinctColours(main) > 100, "the main window rendered (almost) empty");
        Save(main, "main.png");

        var about = new AboutWindow();
        var card = Render((FrameworkElement)about.Content, 460, double.NaN);
        Assert.True(DistinctColours(card) > 20, "the About card rendered (almost) empty");
        Save(card, "about.png");
    }

    private static MainViewModel DemoViewModel(TempDir tmp)
    {
        var store = new ConfigService(tmp.Sub("settings"));
        var config = new BackupConfig
        {
            KeepCount = 5,
            EnableFallback = true,
            FallbackPath = @"E:\Backups",
            ScheduleType = ScheduleType.Weekly,
            ScheduleWeekDay = DayOfWeek.Friday,
            ScheduleHour = 21,
            ScheduleMinute = 30,
            RunOnStartup = true,
        };
        config.Pairs.Add(new BackupPair { Name = "Documents", SourcePath = @"D:\Data\Documents", DestinationPath = @"\\nas\backups\laptop" });
        config.Pairs.Add(new BackupPair { Name = "Photos", SourcePath = @"D:\Data\Photos", DestinationPath = @"\\nas\backups\photos" });
        config.Pairs.Add(new BackupPair { Name = "Projects", SourcePath = @"D:\Data\Projects", DestinationPath = @"E:\Backups", Enabled = false });
        store.Save(config);

        var vm = new MainViewModel(store, useTaskScheduler: false);
        var time = new DateTime(2026, 10, 9, 21, 30, 0);
        foreach (var (seconds, level, message) in DemoLog)
            vm.AddLog(new LogEntry { Timestamp = time.AddSeconds(seconds), Level = level, Message = message });
        vm.StatusText = "Finished 21:34: 1 complete, 1 incomplete";
        return vm;
    }

    // Same wording as the engine writes it, for a run with one complete and one incomplete backup.
    private static readonly (int Seconds, LogLevel Level, string Message)[] DemoLog =
    {
        (0, LogLevel.Info, "▶  Started: Documents"),
        (0, LogLevel.Info, @"   Writing to: \\nas\backups\laptop\Documents_2026-10-09_213000.partial"),
        (38, LogLevel.Success, "   Copied 1284 files, 412.6 MB → Documents_2026-10-09_213000"),
        (39, LogLevel.Info, "   Deleted old backup: Documents_2026-09-04_213000"),
        (39, LogLevel.Success, "✓  Done: Documents (38.9 s)"),
        (39, LogLevel.Info, "▶  Started: Photos"),
        (49, LogLevel.Warning, @"⚠  Destination not reachable, using the fallback folder: E:\Backups"),
        (49, LogLevel.Info, @"   Writing to: E:\Backups\Photos_2026-10-09_213049.partial"),
        (131, LogLevel.Warning, @"   Skipped file: D:\Data\Photos\2026\IMG_4711.CR3: The process cannot access the file because it is being used by another process."),
        (252, LogLevel.Success, "   Copied 3410 files, 8.21 GB → Photos_2026-10-09_213049_incomplete"),
        (252, LogLevel.Warning, "⚠  Done with problems: Photos: 1 item(s) skipped, the backup is marked incomplete (213.4 s)"),
    };

    /// <summary>Lays out a window's content at the given size and renders it, without showing any window.</summary>
    internal static RenderTargetBitmap Render(FrameworkElement content, double width, double height)
    {
        if (content.Parent is Window window)
        {
            var dataContext = window.DataContext;
            window.Content = null;
            content.DataContext = dataContext;
        }
        // NaN height: as tall as the content wants (SizeToContent windows).
        content.Measure(new Size(width, double.IsNaN(height) ? double.PositiveInfinity : height));
        if (double.IsNaN(height))
            height = Math.Ceiling(content.DesiredSize.Height);
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        // Let pending bindings and layout passes run before taking the picture.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        return bitmap;
    }

    internal static int DistinctColours(BitmapSource bitmap)
    {
        var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels.Distinct().Count();
    }

    internal static void Save(BitmapSource bitmap, string fileName)
    {
        var folder = Environment.GetEnvironmentVariable("BOH_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(folder))
            return;
        Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, fileName));
        encoder.Save(file);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.WebView.Desktop;
using Microsoft.Extensions.DependencyInjection;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.ViewModels;
using YoutubeDownloader.ViewModels.Components;
using YoutubeDownloader.ViewModels.Dialogs;
using YoutubeDownloader.Views.Dialogs;

internal static class UiSmoke
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunOnUiThread();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new Exception("UI initialization failed", failure);
    }

    private static void RunOnUiThread()
    {
        using var directory = new TestDirectory();
        var settings = new SettingsService(directory.Path);
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        var builder = AppBuilder
            .Configure(() => new YoutubeDownloader.App(settings))
            .UsePlatformDetect()
            .UseDesktopWebView()
            .SetupWithLifetime(lifetime);
        using var app = (YoutubeDownloader.App)builder.Instance!;
        var window = lifetime.MainWindow ?? throw new Exception("Main window was not created");
        var main = (MainViewModel)window.DataContext!;
        var originalContent = window.Content;
        using var viewModel = new SettingsViewModel(settings);
        var view = new SettingsView { DataContext = viewModel };
        foreach (var language in new[] { "tr", "en" })
        {
            settings.Language = language;
            window.Content = view;
            window.ApplyTemplate();
            view.ApplyTemplate();
            window.Measure(new Size(720, 400));
            window.Arrange(new Rect(0, 0, 720, 400));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            view.Measure(new Size(720, 400));
            view.Arrange(new Rect(0, 0, 440, 400));
            var labels = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text)
                .ToArray();
            if (!labels.Contains(Localization.Get("Language")))
                throw new Exception(
                    "Language label was not translated: "
                        + language
                        + ": "
                        + string.Join(" | ", labels)
                );
            if (!labels.Contains(Localization.Get("Settings")))
                throw new Exception("Settings label was not translated: " + language);
            if (view.DesiredSize.Height > 650)
                throw new Exception("Settings view exceeded its height limit");
        }
        using var selection = new DownloadMultipleSetupViewModel(null!, null!, settings);
        var playlist = new DownloadMultipleSetupView { DataContext = selection };
        window.Content = playlist;
        playlist.Measure(new Size(720, 400));
        playlist.Arrange(new Rect(0, 0, 500, 400));
        var single = new DownloadSingleSetupView();
        single.Measure(new Size(720, 400));
        // Authentication is constructed but never shown/navigated during tests.
        var authentication = new AuthSetupView();
        window.Content = originalContent;
        using var sample = new DownloadViewModel(
            null!,
            null!,
            new DownloadQueue(
                new ControlledExecutor(),
                directory.File("preview-queue.json"),
                new YoutubeDownloader.Core.Utils.DiagnosticLog(directory.File("preview.log"))
            )
        );
        sample.Apply(
            new DownloadJob
            {
                Request = new DownloadRequest
                {
                    Title =
                        "Çok uzun video başlığı — Türkçe karakterler ve birden fazla satıra sığmayacak kadar uzun metin ile görünüm kontrolü",
                    VideoId = "jNQXAC9IVRw",
                    FilePath = directory.File(
                        "Çok uzun video başlığı ve Türkçe karakterler ile dosya görünümü kontrolü.mp4"
                    ),
                },
                Status = QueueStatus.Failed,
                Failure = DownloadFailureKind.Network,
            }
        );
        main.Dashboard.Downloads.Add(sample);
        window.Measure(new Size(920, 700));
        window.Arrange(new Rect(0, 0, 920, 700));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        if (
            Environment.GetEnvironmentVariable("YD_UI_SNAPSHOT_DIR") is
            { Length: > 0 } snapshotDirectory
        )
        {
            Directory.CreateDirectory(snapshotDirectory);
            settings.Language = "tr";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            // Lay out real templates without displaying an interactive window to the user.
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Position = new PixelPoint(-10000, -10000);
            window.Show();
            var root = (Control)originalContent!;
            root.Measure(new Size(920, 700));
            root.Arrange(new Rect(0, 0, 920, 700));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            using var bitmap = new RenderTargetBitmap(
                new PixelSize(1840, 1400),
                new Vector(192, 192)
            );
            bitmap.Render((Avalonia.Visual)originalContent!);
            bitmap.Save(Path.Combine(snapshotDirectory, "dashboard-tr-200dpi.png"));
        }
        main.Dashboard.Downloads.Remove(sample);
        main.ShutdownAsync().GetAwaiter().GetResult();
        window.Close();
    }
}

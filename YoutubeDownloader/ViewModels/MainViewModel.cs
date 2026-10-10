using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Core.Utils;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils.Extensions;
using YoutubeDownloader.ViewModels.Components;

namespace YoutubeDownloader.ViewModels;

public partial class MainViewModel(
    ViewModelManager viewModelManager,
    DialogManager dialogManager,
    SnackbarManager snackbarManager,
    SettingsService settingsService,
    UpdateService updateService
) : ViewModelBase
{
    public string Title => $"{Program.Name} v{Program.VersionString}";
    public DashboardViewModel Dashboard { get; } = viewModelManager.CreateDashboardViewModel();
    private Task? _shutdown;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (settingsService.PersistenceError is { } error)
            snackbarManager.Notify(Localization.Get(error));
        if (!FFmpeg.IsAvailable())
        {
            var missing = viewModelManager.CreateMessageBoxViewModel(
                Localization.Get("FFmpegMissing"),
                Localization.Get("FFmpegMissingMessage"),
                Localization.Get("DOWNLOAD"),
                Localization.Get("CLOSE")
            );
            if (await dialogManager.ShowDialogAsync(missing) == true)
                ProcessExtensions.StartShellExecute("https://ffmpeg.org/download.html");
            return;
        }
        if (Program.IsDevelopmentBuild)
            snackbarManager.Notify(Localization.Get("DevelopmentBuild"));
        await CheckForUpdatesAsync(false);
    }

    [RelayCommand]
    private Task CheckUpdatesAsync() => CheckForUpdatesAsync(true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        try
        {
            var version = await updateService.CheckForUpdatesAsync(manual);
            if (version is null)
            {
                if (manual)
                    snackbarManager.Notify(
                        Localization.Get(
                            Program.IsDevelopmentBuild ? "DevelopmentBuild" : "NoUpdate"
                        )
                    );
                return;
            }
            var dialog = viewModelManager.CreateMessageBoxViewModel(
                Localization.Get("UpdateAvailable"),
                string.Format(
                    Localization.Get("UpdateConsent"),
                    version,
                    Program.ProjectOwner,
                    Program.ProjectRepository
                ),
                Localization.Get("DownloadUpdate"),
                Localization.Get("CANCEL")
            );
            if (await dialogManager.ShowDialogAsync(dialog) != true)
                return;
            snackbarManager.Notify(Localization.Get("DownloadingUpdate"));
            if (!await updateService.PrepareUpdateAsync(version))
            {
                snackbarManager.Notify(Localization.Get("UpdateFailed"));
                return;
            }
            snackbarManager.Notify(
                Localization.Get("UpdateReady"),
                Localization.Get("InstallNow"),
                async () =>
                {
                    await ShutdownAsync();
                    updateService.FinalizeUpdate(true);
                    Application.Current?.ApplicationLifetime?.TryShutdown(2);
                }
            );
        }
        catch (Exception ex)
        {
            new DiagnosticLog(Path.Combine(settingsService.DataDirectory, "diagnostics.log")).Write(
                "update",
                ex
            );
            snackbarManager.Notify(Localization.Get("UpdateFailed"));
        }
    }

    public Task ShutdownAsync() => _shutdown ??= ShutdownCoreAsync();

    private async Task ShutdownCoreAsync()
    {
        await Dashboard.ShutdownAsync();
        settingsService.Save();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Dashboard.Dispose();
            settingsService.Save();
            updateService.FinalizeUpdate(false);
        }
        base.Dispose(disposing);
    }
}

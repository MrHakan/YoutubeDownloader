using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils.Extensions;
using YoutubeDownloader.ViewModels.Components;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.ViewModels.Dialogs;

public partial class DownloadSingleSetupViewModel(
    ViewModelManager viewModelManager,
    DialogManager dialogManager,
    SettingsService settingsService
) : DialogViewModelBase<DownloadViewModel>
{
    [ObservableProperty]
    public partial IVideo? Video { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<VideoDownloadOption>? AvailableDownloadOptions { get; set; }

    [ObservableProperty]
    public partial VideoDownloadOption? SelectedDownloadOption { get; set; }

    [RelayCommand]
    private void Initialize()
    {
        var profile = settingsService.Profiles.FirstOrDefault(p =>
            p.Id == settingsService.SelectedProfileId
        );
        SelectedDownloadOption =
            AvailableDownloadOptions is not null && profile is not null
                ? new VideoDownloadPreference(
                    new YoutubeExplode.Videos.Streams.Container(profile.Container),
                    profile.Quality
                ).TryGetBestOption(AvailableDownloadOptions)
                : null;
        SelectedDownloadOption ??=
            AvailableDownloadOptions?.FirstOrDefault(o =>
                o.Container == settingsService.LastContainer
            ) ?? AvailableDownloadOptions?.FirstOrDefault();
    }

    [RelayCommand]
    private async Task CopyTitleAsync()
    {
        if (Application.Current?.ApplicationLifetime?.TryGetTopLevel()?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(Video?.Title);
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Video is null || SelectedDownloadOption is null)
            return;

        var container = SelectedDownloadOption.Container;

        var filePath = await dialogManager.PromptSaveFilePathAsync(
            [
                new FilePickerFileType($"{container.Name} {Localization.Get("File")}")
                {
                    Patterns = [$"*.{container.Name}"],
                },
            ],
            FileNameTemplate.Apply(settingsService.FileNameTemplate, Video, container)
        );

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        settingsService.LastContainer = container;
        settingsService.LastDownloadDirectory = Path.GetDirectoryName(filePath)!;
        settingsService.Save();
        var download = viewModelManager.CreateDownloadViewModel(
            Video,
            SelectedDownloadOption,
            filePath
        );
        download.Overwrite = File.Exists(filePath);
        Close(download);
    }
}

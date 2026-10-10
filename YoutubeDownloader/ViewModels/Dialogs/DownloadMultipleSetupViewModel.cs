using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils.Extensions;
using YoutubeDownloader.ViewModels.Components;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.ViewModels.Dialogs;

public partial class DownloadMultipleSetupViewModel(
    ViewModelManager viewModelManager,
    DialogManager dialogManager,
    SettingsService settingsService
) : DialogViewModelBase<IReadOnlyList<DownloadViewModel>>
{
    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredVideos))]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    public partial IReadOnlyList<IVideo>? AvailableVideos { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredVideos))]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    public partial string Filter { get; set; } = "";
    public IReadOnlyList<IVideo> FilteredVideos =>
        AvailableVideos
            ?.Where(v => v.Title.Contains(Filter ?? "", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];

    private static bool IsUnavailable(IVideo video) =>
        video.Title is "[Private video]" or "[Deleted video]";

    public string SelectionSummary =>
        string.Format(
            Localization.Get("SelectionSummary"),
            FilteredVideos.Count,
            AvailableVideos?.Count ?? 0,
            SelectedVideos.Count,
            AvailableVideos?.Count(IsUnavailable) ?? 0
        );

    [RelayCommand]
    private void SelectVisible()
    {
        SelectedVideos.Clear();
        foreach (var video in FilteredVideos.Where(v => !IsUnavailable(v)))
            SelectedVideos.Add(video);
    }

    [ObservableProperty]
    public partial Container SelectedContainer { get; set; } = Container.Mp4;

    [ObservableProperty]
    public partial VideoQualityPreference SelectedVideoQualityPreference { get; set; } =
        VideoQualityPreference.Highest;

    public ObservableCollection<IVideo> SelectedVideos { get; } = [];

    public IReadOnlyList<Container> AvailableContainers { get; } =
    [Container.Mp4, Container.WebM, Container.Mp3, new("ogg")];

    public IReadOnlyList<VideoQualityPreference> AvailableVideoQualityPreferences { get; } =
        // Without .AsEnumerable(), the below line throws a compile-time error starting with .NET SDK v9.0.200
        Enum.GetValues<VideoQualityPreference>().AsEnumerable().Reverse().ToArray();

    [RelayCommand]
    private void Initialize()
    {
        var profile = settingsService.Profiles.FirstOrDefault(p =>
            p.Id == settingsService.SelectedProfileId
        );
        SelectedContainer = profile is not null
            ? new Container(profile.Container)
            : settingsService.LastContainer;
        SelectedVideoQualityPreference =
            profile?.Quality ?? settingsService.LastVideoQualityPreference;
        SelectedVideos.CollectionChanged += (_, _) =>
        {
            ConfirmCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SelectionSummary));
        };
    }

    [RelayCommand]
    private async Task CopyTitleAsync()
    {
        if (Application.Current?.ApplicationLifetime?.TryGetTopLevel()?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(Title);
    }

    private bool CanConfirm() => SelectedVideos.Any();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        var dirPath = await dialogManager.PromptDirectoryPathAsync(
            settingsService.LastDownloadDirectory
        );
        if (string.IsNullOrWhiteSpace(dirPath))
            return;

        var downloads = new List<DownloadViewModel>();
        for (var i = 0; i < SelectedVideos.Count; i++)
        {
            var video = SelectedVideos[i];

            var baseFilePath = Path.Combine(
                dirPath,
                FileNameTemplate.Apply(
                    settingsService.FileNameTemplate,
                    video,
                    SelectedContainer,
                    (i + 1).ToString().PadLeft(SelectedVideos.Count.ToString().Length, '0')
                )
            );

            if (settingsService.ShouldSkipExistingFiles && File.Exists(baseFilePath))
                continue;

            var filePath = PathExtensions.EnsureUniqueFilePath(baseFilePath);

            downloads.Add(
                viewModelManager.CreateDownloadViewModel(
                    video,
                    new VideoDownloadPreference(SelectedContainer, SelectedVideoQualityPreference),
                    filePath
                )
            );
        }

        settingsService.LastContainer = SelectedContainer;
        settingsService.LastVideoQualityPreference = SelectedVideoQualityPreference;
        settingsService.LastDownloadDirectory = dirPath;
        settingsService.Save();

        Close(downloads);
    }
}

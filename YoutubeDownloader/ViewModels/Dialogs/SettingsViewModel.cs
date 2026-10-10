using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils;
using YoutubeDownloader.Utils.Extensions;

namespace YoutubeDownloader.ViewModels.Dialogs;

public partial class SettingsViewModel : DialogViewModelBase
{
    private readonly SettingsService _settingsService;

    private readonly DisposableCollector _eventRoot = new();

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;

        _eventRoot.Add(_settingsService.WatchAllProperties(OnAllPropertiesChanged));
        SelectedProfile = Profiles[0];
    }

    public IReadOnlyList<ThemeVariant> AvailableThemes => Enum.GetValues<ThemeVariant>();

    public IReadOnlyList<string> AvailableLanguages { get; } = ["Türkçe", "English"];
    public string SelectedLanguage
    {
        get => _settingsService.Language == "en" ? "English" : "Türkçe";
        set => _settingsService.Language = value == "English" ? "en" : "tr";
    }
    public IReadOnlyList<DownloadProfile> Profiles => _settingsService.Profiles.ToArray();
    public IReadOnlyList<string> AvailableProfileContainers { get; } =
    ["mp4", "webm", "mp3", "ogg"];
    public IReadOnlyList<VideoQualityPreference> AvailableQualities { get; } =
        Enum.GetValues<VideoQualityPreference>();

    [ObservableProperty]
    public partial DownloadProfile? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string ProfileName { get; set; } = "";

    [ObservableProperty]
    public partial string ProfileContainer { get; set; } = "mp4";

    [ObservableProperty]
    public partial VideoQualityPreference ProfileQuality { get; set; } =
        VideoQualityPreference.Highest;

    partial void OnSelectedProfileChanged(DownloadProfile? value)
    {
        if (value is null)
            return;
        ProfileName = value.Name;
        ProfileContainer = value.Container;
        ProfileQuality = value.Quality;
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (
            SelectedProfile is null
            || string.IsNullOrWhiteSpace(ProfileName)
            || !AvailableProfileContainers.Contains(ProfileContainer)
        )
            return;
        var updated = SelectedProfile with
        {
            Name = ProfileName.Trim(),
            Container = ProfileContainer,
            Quality = ProfileQuality,
        };
        _settingsService.Profiles = Profiles
            .Select(p => p.Id == updated.Id ? updated : p)
            .ToArray();
        SelectedProfile = updated;
        _settingsService.Save();
    }

    [RelayCommand]
    private void AddProfile()
    {
        if (Profiles.Count >= 30)
            return;
        var profile = new DownloadProfile(
            Guid.NewGuid().ToString("N"),
            Localization.Get("New profile"),
            "mp4",
            VideoQualityPreference.Highest
        );
        _settingsService.Profiles = [.. Profiles, profile];
        SelectedProfile = profile;
        _settingsService.Save();
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (SelectedProfile is null || Profiles.Count < 2)
            return;
        _settingsService.Profiles = Profiles.Where(p => p.Id != SelectedProfile.Id).ToArray();
        SelectedProfile = Profiles[0];
        if (!Profiles.Any(p => p.Id == _settingsService.SelectedProfileId))
            _settingsService.SelectedProfileId = Profiles[0].Id;
        _settingsService.Save();
    }

    public ThemeVariant Theme
    {
        get => _settingsService.Theme;
        set => _settingsService.Theme = value;
    }

    public bool IsAutoUpdateEnabled
    {
        get => _settingsService.IsAutoUpdateEnabled;
        set => _settingsService.IsAutoUpdateEnabled = value;
    }

    public bool IsAuthPersisted
    {
        get => _settingsService.IsAuthPersisted;
        set => _settingsService.IsAuthPersisted = value;
    }

    public bool ShouldInjectLanguageSpecificAudioStreams
    {
        get => _settingsService.ShouldInjectLanguageSpecificAudioStreams;
        set => _settingsService.ShouldInjectLanguageSpecificAudioStreams = value;
    }

    public bool ShouldInjectSubtitles
    {
        get => _settingsService.ShouldInjectSubtitles;
        set => _settingsService.ShouldInjectSubtitles = value;
    }

    public bool ShouldInjectTags
    {
        get => _settingsService.ShouldInjectTags;
        set => _settingsService.ShouldInjectTags = value;
    }

    public bool ShouldSkipExistingFiles
    {
        get => _settingsService.ShouldSkipExistingFiles;
        set => _settingsService.ShouldSkipExistingFiles = value;
    }

    public string FileNameTemplate
    {
        get => _settingsService.FileNameTemplate;
        set => _settingsService.FileNameTemplate = value;
    }

    public int ParallelLimit
    {
        get => _settingsService.ParallelLimit;
        set => _settingsService.ParallelLimit = Math.Clamp(value, 1, 10);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _eventRoot.Dispose();
            _settingsService.Save();
        }

        base.Dispose(disposing);
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gress;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Core.Resolving;
using YoutubeDownloader.Core.Utils;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils;
using YoutubeDownloader.Utils.Extensions;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.ViewModels.Components;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ViewModelManager _viewModelManager;
    private readonly SnackbarManager _snackbarManager;
    private readonly DialogManager _dialogManager;
    private readonly SettingsService _settingsService;
    private readonly DownloadQueue _queue;
    private readonly DisposableCollector _eventRoot = new();
    private string? _lastStorageError;

    public DashboardViewModel(
        ViewModelManager viewModelManager,
        SnackbarManager snackbarManager,
        DialogManager dialogManager,
        SettingsService settingsService,
        DownloadQueue queue
    )
    {
        _viewModelManager = viewModelManager;
        _snackbarManager = snackbarManager;
        _dialogManager = dialogManager;
        _settingsService = settingsService;
        _queue = queue;
        _queue.Changed += QueueChanged;
        _eventRoot.Add(
            settingsService.WatchProperty(
                o => o.ParallelLimit,
                () => queue.Limit = settingsService.ParallelLimit,
                true
            )
        );
        _eventRoot.Add(
            settingsService.WatchProperty(
                o => o.Profiles,
                () =>
                {
                    OnPropertyChanged(nameof(Profiles));
                    OnPropertyChanged(nameof(SelectedProfile));
                }
            )
        );
        Localization.Changed += LanguageChanged;
        _eventRoot.Add(
            settingsService.WatchProperty(
                o => o.SelectedProfileId,
                () => OnPropertyChanged(nameof(SelectedProfile))
            )
        );
        _eventRoot.Add(
            settingsService.WatchProperty(
                o => o.LastDownloadDirectory,
                () => OnPropertyChanged(nameof(DownloadDirectory))
            )
        );
    }

    private void LanguageChanged() => OnAllPropertiesChanged();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    public partial string? Query { get; set; }

    [ObservableProperty]
    public partial bool QuickMode { get; set; }
    public ProgressContainer<Percentage> Progress { get; } = new();
    public bool IsProgressIndeterminate => IsBusy;
    public ObservableCollection<DownloadViewModel> Downloads { get; } = [];
    public IReadOnlyList<DownloadProfile> Profiles => _settingsService.Profiles.ToArray();
    public DownloadProfile? SelectedProfile
    {
        get => Profiles.FirstOrDefault(p => p.Id == _settingsService.SelectedProfileId);
        set
        {
            if (value is null)
                return;
            _settingsService.SelectedProfileId = value.Id;
            OnPropertyChanged();
            _settingsService.Save();
        }
    }
    public string DownloadDirectory => _settingsService.LastDownloadDirectory;

    public void RestoreQueue()
    {
        _queue.Load();
        foreach (var job in _queue.Jobs)
            SyncJob(job);
        NotifyStorageError();
    }

    private void QueueChanged(DownloadJob job) => Dispatcher.UIThread.Post(() => SyncJob(job));

    private void SyncJob(DownloadJob job)
    {
        if (!_queue.Jobs.Contains(job))
            return;
        var vm = Downloads.FirstOrDefault(d => d.JobId == job.Request.Id);
        if (vm is null)
        {
            vm = _viewModelManager.CreateDownloadViewModel(job);
            Downloads.Add(vm);
        }
        vm.Apply(job);
        var wanted = _queue.Jobs.ToList().IndexOf(job);
        var current = Downloads.IndexOf(vm);
        if (wanted >= 0 && current != wanted && wanted < Downloads.Count)
            Downloads.Move(current, wanted);
        NotifyStorageError();
    }

    private void NotifyStorageError()
    {
        if (_queue.StorageError is { } error && error != _lastStorageError)
            _snackbarManager.Notify(Localization.Get(error));
        _lastStorageError = _queue.StorageError;
    }

    [RelayCommand]
    private async Task ShowAuthSetupAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.CreateAuthSetupViewModel());

    [RelayCommand]
    private async Task ShowSettingsAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.CreateSettingsViewModel());

    [RelayCommand]
    private void ResumeInterruptedDownloads()
    {
        foreach (var job in _queue.Jobs.Where(j => j.Status == QueueStatus.Interrupted))
            _queue.Restart(job.Request.Id);
    }

    [RelayCommand]
    private void MoveUp(DownloadViewModel item) => _queue.Move(item.JobId, -1);

    [RelayCommand]
    private void MoveDown(DownloadViewModel item) => _queue.Move(item.JobId, 1);

    [RelayCommand]
    private async Task ChooseDirectoryAsync()
    {
        var path = await _dialogManager.PromptDirectoryPathAsync(DownloadDirectory);
        if (path is null)
            return;
        _settingsService.LastDownloadDirectory = path;
        _settingsService.Save();
        OnPropertyChanged(nameof(DownloadDirectory));
    }

    [RelayCommand]
    private void OpenDirectory()
    {
        try
        {
            if (Directory.Exists(DownloadDirectory))
                ProcessExtensions.StartShellExecute(DownloadDirectory);
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify(Localization.Get("ErrorFileSystem"));
            Log.Write("open-directory", ex);
        }
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        var dialog = _viewModelManager.CreateMessageBoxViewModel(
            Localization.Get("About"),
            $"{Program.Name} v{Program.VersionString}\nMrHakan\n\n"
                + Localization.Get("AboutDetails"),
            Localization.Get("Releases"),
            Localization.Get("CLOSE")
        );
        if (await _dialogManager.ShowDialogAsync(dialog) == true)
            ProcessExtensions.StartShellExecute(Program.ProjectReleasesUrl);
    }

    private DiagnosticLog Log =>
        new(Path.Combine(_settingsService.DataDirectory, "diagnostics.log"));

    private DownloadViewModel CreateProfileDownload(IVideo video)
    {
        var profile = SelectedProfile ?? Profiles[0];
        var container = new Container(profile.Container);
        return _viewModelManager.CreateDownloadViewModel(
            video,
            new VideoDownloadPreference(container, profile.Quality),
            Path.Combine(
                DownloadDirectory,
                FileNameTemplate.Apply(_settingsService.FileNameTemplate, video, container)
            )
        );
    }

    private void EnqueueDownload(DownloadViewModel download)
    {
        var option = download.DownloadOption;
        var preference = download.DownloadPreference;
        var request = new DownloadRequest
        {
            VideoId = download.Video!.Id,
            Title = download.Video.Title,
            FilePath = download.FilePath!,
            Container = option?.Container.Name ?? preference!.PreferredContainer.Name,
            AudioOnly = option?.IsAudioOnly ?? preference!.PreferredContainer.IsAudioOnly,
            Quality = preference?.PreferredVideoQuality ?? VideoQualityPreference.Highest,
            ExactQuality = option?.VideoQuality?.Label,
            AudioLanguages =
                option
                    ?.StreamInfos.OfType<IAudioStreamInfo>()
                    .Where(s => s.AudioLanguage is not null)
                    .Select(s => s.AudioLanguage!.Value.Code)
                    .Distinct()
                    .ToArray() ?? [],
            IncludeLanguages = _settingsService.ShouldInjectLanguageSpecificAudioStreams,
            IncludeSubtitles = _settingsService.ShouldInjectSubtitles,
            IncludeTags = _settingsService.ShouldInjectTags,
            Overwrite = download.Overwrite,
        };
        var job = _queue.Enqueue(request, download.Video);
        download.Dispose();
        SyncJob(job);
    }

    private bool CanProcessQuery() => !IsBusy && !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanProcessQuery), IncludeCancelCommand = true)]
    private async Task ProcessQueryAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Query))
            return;
        IsBusy = true;
        try
        {
            using var resolver = new QueryResolver(_settingsService.LastAuthCookies);
            var queries = Query.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            var results = new List<QueryResult>();
            foreach (var (index, query) in queries.Index())
            {
                try
                {
                    results.Add(await resolver.ResolveAsync(query, cancellationToken));
                }
                catch (YoutubeExplodeException ex)
                    when (queries.Length > 1
                        && ex is VideoUnavailableException or PlaylistUnavailableException
                    )
                {
                    _snackbarManager.Notify(Localization.Get("UnavailableSkipped"));
                }
                Progress.Report(Percentage.FromFraction((index + 1.0) / queries.Length));
            }
            var result = QueryResult.Aggregate(results);
            if (result.Videos.Count == 0)
            {
                await _dialogManager.ShowDialogAsync(
                    _viewModelManager.CreateMessageBoxViewModel(
                        Localization.Get("Nothing found"),
                        Localization.Get("NothingFoundMessage")
                    ),
                    cancellationToken
                );
                return;
            }
            if (QuickMode && result.Kind != QueryResultKind.Search)
            {
                if (string.IsNullOrWhiteSpace(DownloadDirectory))
                    await ChooseDirectoryAsync();
                if (string.IsNullOrWhiteSpace(DownloadDirectory))
                    return;
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var video in result.Videos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var download = CreateProfileDownload(video);
                    if (_settingsService.ShouldSkipExistingFiles && File.Exists(download.FilePath))
                    {
                        download.Dispose();
                        continue;
                    }
                    EnqueueDownload(download);
                }
            }
            else if (result.Videos.Count == 1)
            {
                var video = result.Videos.Single();
                using var downloader = new VideoDownloader(_settingsService.LastAuthCookies);
                var options = await downloader.GetDownloadOptionsAsync(
                    video.Id,
                    _settingsService.ShouldInjectLanguageSpecificAudioStreams,
                    cancellationToken
                );
                var download = await _dialogManager.ShowDialogAsync(
                    _viewModelManager.CreateDownloadSingleSetupViewModel(video, options),
                    cancellationToken
                );
                if (download is null)
                    return;
                EnqueueDownload(download);
            }
            else
            {
                var title = result.Title;
                foreach (var prefix in new[] { "Playlist", "Channel", "Search" })
                    if (title.StartsWith(prefix + ": ", StringComparison.Ordinal))
                        title = Localization.Get(prefix) + title[prefix.Length..];
                var downloads = await _dialogManager.ShowDialogAsync(
                    _viewModelManager.CreateDownloadMultipleSetupViewModel(
                        title,
                        result.Videos,
                        result.Kind is not QueryResultKind.Search and not QueryResultKind.Aggregate
                    ),
                    cancellationToken
                );
                if (downloads is null)
                    return;
                foreach (var download in downloads)
                    EnqueueDownload(download);
            }
            Query = "";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Write("resolve-query", ex);
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.CreateMessageBoxViewModel(
                    Localization.Get("Error"),
                    Localization.Get("Error" + DownloadFailure.Classify(ex))
                )
            );
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RemoveDownload(DownloadViewModel item)
    {
        if (!_queue.Remove(item.JobId))
        {
            _queue.Cancel(item.JobId);
            return;
        }
        Downloads.Remove(item);
        item.Dispose();
    }

    [RelayCommand]
    private void RemoveSuccessfulDownloads()
    {
        foreach (var item in Downloads.Where(d => d.Status == DownloadStatus.Completed).ToArray())
            RemoveDownload(item);
    }

    [RelayCommand]
    private void RemoveInactiveDownloads()
    {
        foreach (
            var item in Downloads
                .Where(d => d.Status is not (DownloadStatus.Started or DownloadStatus.Enqueued))
                .ToArray()
        )
            RemoveDownload(item);
    }

    [RelayCommand]
    private void RestartDownload(DownloadViewModel item) => _queue.Restart(item.JobId);

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ChangeFormatAsync(
        DownloadViewModel item,
        CancellationToken cancellationToken
    )
    {
        var job = _queue.Jobs.FirstOrDefault(j => j.Request.Id == item.JobId);
        if (job is null || job.Status is QueueStatus.Running or QueueStatus.Queued)
            return;
        try
        {
            using var resolver = new QueryResolver(_settingsService.LastAuthCookies);
            var result = await resolver.ResolveAsync(
                "https://www.youtube.com/watch?v=" + job.Request.VideoId,
                cancellationToken
            );
            var video = result.Videos.Single();
            using var downloader = new VideoDownloader(_settingsService.LastAuthCookies);
            var options = await downloader.GetDownloadOptionsAsync(
                video.Id,
                _settingsService.ShouldInjectLanguageSpecificAudioStreams,
                cancellationToken
            );
            var download = await _dialogManager.ShowDialogAsync(
                _viewModelManager.CreateDownloadSingleSetupViewModel(video, options),
                cancellationToken
            );
            if (download is not null)
            {
                RemoveDownload(item);
                EnqueueDownload(download);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Write("change-format", ex);
            _snackbarManager.Notify(Localization.Get("Error" + DownloadFailure.Classify(ex)));
        }
    }

    [RelayCommand]
    private void RestartFailedDownloads()
    {
        foreach (var item in Downloads.Where(d => d.Status == DownloadStatus.Failed).ToArray())
            _queue.Restart(item.JobId);
    }

    [RelayCommand]
    private void CancelAllDownloads()
    {
        foreach (var job in _queue.Jobs)
            _queue.Cancel(job.Request.Id);
    }

    public async Task ShutdownAsync()
    {
        ProcessQueryCancelCommand.Execute(null);
        ChangeFormatCancelCommand.Execute(null);
        await _queue.ShutdownAsync();
        var pending = new[] { ProcessQueryCommand.ExecutionTask, ChangeFormatCommand.ExecutionTask }
            .OfType<Task>()
            .ToArray();
        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException ex)
        {
            Log.Write("close-native-picker", ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _queue.Changed -= QueueChanged;
            Localization.Changed -= LanguageChanged;
            _eventRoot.Dispose();
            foreach (var item in Downloads)
                item.Dispose();
        }
        base.Dispose(disposing);
    }
}

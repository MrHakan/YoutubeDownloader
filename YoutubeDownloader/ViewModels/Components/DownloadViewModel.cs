using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gress;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils;
using YoutubeDownloader.Utils.Extensions;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.ViewModels.Components;

public partial class DownloadViewModel : ViewModelBase
{
    private readonly ViewModelManager _viewModelManager;
    private readonly DialogManager _dialogManager;

    private readonly DisposableCollector _eventRoot = new();
    private readonly DownloadQueue _queue;
    private DownloadJob? _job;

    private bool _isDisposed;

    public Guid JobId { get; private set; }
    public bool Overwrite { get; set; }
    public string Title => Video?.Title ?? _job?.Request.Title ?? FileName ?? "";
    public string WarningText => string.Join("\n", _job?.Warnings.Select(Localization.Get) ?? []);
    public string DetailText => ErrorMessage ?? WarningText;
    public string StatusText
    {
        get
        {
            if (_job is null)
                return "";
            if (_job.Status != QueueStatus.Running)
                return Localization.Get(_job.Status.ToString())
                    + (_job.Warnings.Length > 0 ? " ⚠" : "");
            var p = _job.Progress;
            var text = Localization.Get(p.Stage.ToString());
            if (p.Stage == DownloadStage.Retrying)
                return text + $" ({p.Fraction:0}/3)";
            if (p.Stage == DownloadStage.Downloading && p.BytesPerSecond is { } speed)
                return text
                    + $" {speed / 1024 / 1024:0.0} MB/s"
                    + (p.Remaining is { } remaining ? $" • {remaining:mm\\:ss}" : "");
            return text;
        }
    }

    public void Apply(DownloadJob job)
    {
        _job = job;
        JobId = job.Request.Id;
        Video = job.Video;
        FilePath = job.Request.FilePath;
        Status = job.Status switch
        {
            QueueStatus.Running => DownloadStatus.Started,
            QueueStatus.Completed => DownloadStatus.Completed,
            QueueStatus.Failed => DownloadStatus.Failed,
            QueueStatus.Canceled => DownloadStatus.Canceled,
            QueueStatus.Interrupted => DownloadStatus.Interrupted,
            _ => DownloadStatus.Enqueued,
        };
        ErrorMessage = job.Failure is { } failure ? Localization.Get("Error" + failure) : null;
        Progress.Report(Percentage.FromFraction(Math.Clamp(job.Progress.Fraction, 0, 1)));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(WarningText));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DetailText));
    }

    private void LanguageChanged()
    {
        if (_job is not null)
            Apply(_job);
        OnAllPropertiesChanged();
    }

    [ObservableProperty]
    public partial IVideo? Video { get; set; }

    [ObservableProperty]
    public partial VideoDownloadOption? DownloadOption { get; set; }

    [ObservableProperty]
    public partial VideoDownloadPreference? DownloadPreference { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCanceledOrFailed))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand))]
    public partial DownloadStatus Status { get; set; } = DownloadStatus.Enqueued;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyErrorMessageCommand))]
    public partial string? ErrorMessage { get; set; }

    public DownloadViewModel(
        ViewModelManager viewModelManager,
        DialogManager dialogManager,
        DownloadQueue queue
    )
    {
        _viewModelManager = viewModelManager;
        _dialogManager = dialogManager;
        _queue = queue;
        Localization.Changed += LanguageChanged;

        _eventRoot.Add(
            Progress.WatchProperty(
                o => o.Current,
                () => OnPropertyChanged(nameof(IsProgressIndeterminate))
            )
        );
    }

    public string? FileName => Path.GetFileName(FilePath);

    public ProgressContainer<Percentage> Progress { get; } = new();

    public bool IsProgressIndeterminate => Progress.Current.Fraction is <= 0 or >= 1;

    public bool IsCanceledOrFailed =>
        Status is DownloadStatus.Canceled or DownloadStatus.Failed or DownloadStatus.Interrupted;

    private bool CanCancel() => Status is DownloadStatus.Enqueued or DownloadStatus.Started;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (_isDisposed)
            return;

        _queue.Cancel(JobId);
    }

    private bool CanShowFile() =>
        Status == DownloadStatus.Completed
        // This only works on Windows currently
        && OperatingSystem.IsWindows();

    [RelayCommand(CanExecute = nameof(CanShowFile))]
    private async Task ShowFileAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            return;

        try
        {
            // Navigate to the file in Windows Explorer
            ProcessExtensions.Start("explorer", ["/select,", FilePath]);
        }
        catch (Exception ex)
        {
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.CreateMessageBoxViewModel("Error", ex.Message)
            );
        }
    }

    private bool CanOpenFile() => Status == DownloadStatus.Completed;

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private async Task OpenFileAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            return;

        try
        {
            ProcessExtensions.StartShellExecute(FilePath);
        }
        catch (Exception ex)
        {
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.CreateMessageBoxViewModel("Error", ex.Message)
            );
        }
    }

    [RelayCommand]
    private async Task CopyErrorMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(ErrorMessage))
            return;

        if (Application.Current?.ApplicationLifetime?.TryGetTopLevel()?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(ErrorMessage);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _eventRoot.Dispose();
            Localization.Changed -= LanguageChanged;

            _isDisposed = true;
        }

        base.Dispose(disposing);
    }
}

using System;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Core.Downloading;

public enum DownloadStage
{
    Resolving,
    Downloading,
    Subtitles,
    Converting,
    Tagging,
    Retrying,
}

public enum QueueStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Canceled,
    Interrupted,
}

public sealed record DownloadRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string VideoId { get; init; } = "";
    public string Title { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string Container { get; init; } = "mp4";
    public bool AudioOnly { get; init; }
    public VideoQualityPreference Quality { get; init; } = VideoQualityPreference.Highest;
    public string? ExactQuality { get; init; }
    public string[] AudioLanguages { get; init; } = [];
    public bool IncludeLanguages { get; init; } = true;
    public bool IncludeSubtitles { get; init; } = true;
    public bool IncludeTags { get; init; } = true;
    public bool Overwrite { get; init; }
}

public sealed record TransferProgress(
    DownloadStage Stage,
    double Fraction = 0,
    double? BytesPerSecond = null,
    TimeSpan? Remaining = null
);

public sealed record DownloadExecutionResult(IVideo? Video = null, string[]? Warnings = null);

public interface IDownloadExecutor
{
    System.Threading.Tasks.Task<DownloadExecutionResult> ExecuteAsync(
        DownloadRequest request,
        IProgress<TransferProgress> progress,
        System.Threading.CancellationToken token
    );
}

public sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}

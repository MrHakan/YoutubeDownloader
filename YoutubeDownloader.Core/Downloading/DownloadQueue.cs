using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDownloader.Core.Utils;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Core.Downloading;

public sealed class DownloadJob
{
    public DownloadRequest Request { get; set; } = new();
    public QueueStatus Status { get; set; }
    public DownloadFailureKind? Failure { get; set; }
    public string[] Warnings { get; set; } = [];

    [JsonIgnore]
    public TransferProgress Progress { get; set; } = new(DownloadStage.Resolving);

    [JsonIgnore]
    public IVideo? Video { get; set; }
}

public sealed record QueueDocument(int Version, List<DownloadJob> Jobs);

public sealed class DownloadQueue(IDownloadExecutor executor, string statePath, DiagnosticLog log)
{
    private readonly object _gate = new();
    private readonly List<DownloadJob> _jobs = [];
    private readonly Dictionary<Guid, (CancellationTokenSource Cancellation, Task Task)> _active =
    [];
    private int _limit = 2;
    private bool _stopping;
    public event Action<DownloadJob>? Changed;
    public string? StorageError { get; private set; }
    public IReadOnlyList<DownloadJob> Jobs
    {
        get
        {
            lock (_gate)
                return _jobs.ToArray();
        }
    }

    public int Limit
    {
        get
        {
            lock (_gate)
                return _limit;
        }
        set
        {
            lock (_gate)
            {
                _limit = Math.Clamp(value, 1, 10);
                Schedule();
            }
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            if (_jobs.Count > 0)
                return;
            try
            {
                var data = AtomicJsonFile.Read<QueueDocument>(statePath);
                if (data is null)
                    return;
                if (data.Version != 1 || data.Jobs is null)
                    throw new JsonException("Unsupported queue version.");
                foreach (var job in data.Jobs.Take(1000))
                {
                    if (
                        job?.Request is null
                        || job.Request.VideoId is null
                        || job.Request.FilePath is null
                    )
                        continue;
                    job.Warnings ??= [];
                    if (
                        VideoId.TryParse(job.Request.VideoId) is null
                        || !Path.IsPathFullyQualified(job.Request.FilePath)
                        || job.Request.Id == Guid.Empty
                        || !new[] { "mp4", "webm", "mp3", "ogg" }.Contains(job.Request.Container)
                    )
                        continue;
                    if (_jobs.Any(j => j.Request.Id == job.Request.Id))
                        continue;
                    job.Request = job.Request with
                    {
                        Title = job.Request.Title ?? job.Request.VideoId,
                        AudioLanguages = job.Request.AudioLanguages ?? [],
                    };
                    if (job.Status is QueueStatus.Running or QueueStatus.Queued)
                        job.Status = QueueStatus.Interrupted;
                    _jobs.Add(job);
                }
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or JsonException
                            or UnauthorizedAccessException
                            or ArgumentException
                )
            {
                StorageError = "QueueLoadFailed";
                log.Write("queue-load", ex);
                // Preserve the unreadable document for recovery, never silently overwrite it.
                try
                {
                    if (File.Exists(statePath))
                        File.Copy(statePath, statePath + ".unreadable", true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public DownloadJob Enqueue(DownloadRequest request, IVideo? video = null)
    {
        lock (_gate)
        {
            if (_stopping)
                throw new InvalidOperationException("Queue is stopping.");
            var duplicate = _jobs.FirstOrDefault(j =>
                j.Status is QueueStatus.Queued or QueueStatus.Running
                && j.Request.VideoId == request.VideoId
                && j.Request.Container == request.Container
                && j.Request.AudioOnly == request.AudioOnly
                && j.Request.ExactQuality == request.ExactQuality
                && j.Request.Quality == request.Quality
                && j.Request.IncludeLanguages == request.IncludeLanguages
                && j.Request.IncludeSubtitles == request.IncludeSubtitles
                && j.Request.IncludeTags == request.IncludeTags
                && j.Request.AudioLanguages.SequenceEqual(request.AudioLanguages)
                && PathEquals(j.Request.FilePath, request.FilePath)
            );
            if (duplicate is not null)
                return duplicate;
            if (_jobs.Any(j => j.Request.Id == request.Id))
                throw new ArgumentException("Duplicate job ID.", nameof(request));
            var path = ReservePath(request.FilePath, request.Overwrite);
            var job = new DownloadJob
            {
                Request = request with
                {
                    FilePath = path,
                    Overwrite = request.Overwrite && PathEquals(path, request.FilePath),
                },
                Video = video,
            };
            _jobs.Add(job);
            Persist();
            Changed?.Invoke(job);
            Schedule();
            return job;
        }
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a),
            Path.GetFullPath(b),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );

    private string ReservePath(string requested, bool overwrite)
    {
        var path = Path.GetFullPath(requested);
        var original = path;
        for (
            var i = 2;
            _jobs.Any(j =>
                j.Status is QueueStatus.Queued or QueueStatus.Running or QueueStatus.Interrupted
                && PathEquals(j.Request.FilePath, path)
            ) || (!overwrite && File.Exists(path));
            i++
        )
        {
            path = Path.Combine(
                Path.GetDirectoryName(original)!,
                $"{Path.GetFileNameWithoutExtension(original)} ({i}){Path.GetExtension(original)}"
            );
            overwrite = false;
        }
        return path;
    }

    public void Restart(Guid id)
    {
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.Request.Id == id);
            if (
                job is null
                || _active.ContainsKey(id)
                || job.Status is QueueStatus.Completed or QueueStatus.Queued
                || _stopping
            )
                return;
            // Re-reserve because another job or an external app may now own this path.
            var original = job.Request.FilePath;
            job.Status = QueueStatus.Canceled;
            var path = ReservePath(original, job.Request.Overwrite);
            job.Request = job.Request with
            {
                FilePath = path,
                Overwrite = job.Request.Overwrite && PathEquals(path, original),
            };
            job.Status = QueueStatus.Queued;
            job.Failure = null;
            job.Warnings = [];
            job.Progress = new(DownloadStage.Resolving);
            Persist();
            Changed?.Invoke(job);
            Schedule();
        }
    }

    public void Cancel(Guid id)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(id, out var active))
            {
                active.Cancellation.Cancel();
                return;
            }
            var job = _jobs.FirstOrDefault(j => j.Request.Id == id);
            if (job?.Status is not (QueueStatus.Queued or QueueStatus.Interrupted))
                return;
            job.Status = QueueStatus.Canceled;
            Persist();
            Changed?.Invoke(job);
        }
    }

    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            if (_active.ContainsKey(id))
                return false;
            var removed = _jobs.RemoveAll(j => j.Request.Id == id) > 0;
            if (removed)
                Persist();
            return removed;
        }
    }

    public void Move(Guid id, int direction)
    {
        lock (_gate)
        {
            var index = _jobs.FindIndex(j => j.Request.Id == id);
            if (index < 0 || _jobs[index].Status != QueueStatus.Queued)
                return;
            var target = Math.Clamp(index + direction, 0, _jobs.Count - 1);
            var job = _jobs[index];
            _jobs.RemoveAt(index);
            _jobs.Insert(target, job);
            Persist();
            Changed?.Invoke(job);
        }
    }

    private void Schedule()
    {
        if (_stopping)
            return;
        while (_active.Count < _limit)
        {
            var job = _jobs.FirstOrDefault(j => j.Status == QueueStatus.Queued);
            if (job is null)
                break;
            job.Status = QueueStatus.Running;
            var cancellation = new CancellationTokenSource();
            // The gate prevents the task from finishing before its entry has been registered.
            var task = Task.Run(() => ExecuteAsync(job, cancellation.Token));
            _active.Add(job.Request.Id, (cancellation, task));
            Persist();
            Changed?.Invoke(job);
        }
    }

    private async Task ExecuteAsync(DownloadJob job, CancellationToken token)
    {
        try
        {
            var result = await executor
                .ExecuteAsync(
                    job.Request,
                    new CallbackProgress<TransferProgress>(p =>
                    {
                        lock (_gate)
                        {
                            job.Progress = p;
                            Changed?.Invoke(job);
                        }
                    }),
                    token
                )
                .ConfigureAwait(false);
            lock (_gate)
            {
                job.Video = result.Video ?? job.Video;
                job.Warnings = result.Warnings ?? [];
                job.Status = QueueStatus.Completed;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            lock (_gate)
                job.Status = _stopping ? QueueStatus.Interrupted : QueueStatus.Canceled;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                job.Status = QueueStatus.Failed;
                job.Failure = DownloadFailure.Classify(ex);
            }
            log.Write($"download {job.Request.Id} {job.Failure}", ex);
        }
        finally
        {
            lock (_gate)
            {
                if (_active.Remove(job.Request.Id, out var active))
                    active.Cancellation.Dispose();
                Persist();
                Changed?.Invoke(job);
                Schedule();
            }
        }
    }

    public async Task ShutdownAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            _stopping = true;
            foreach (var active in _active.Values)
                active.Cancellation.Cancel();
            tasks = _active.Values.Select(a => a.Task).ToArray();
            Persist();
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private void Persist()
    {
        try
        {
            AtomicJsonFile.Write(statePath, new QueueDocument(1, _jobs));
            StorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StorageError = "QueueSaveFailed";
            log.Write("queue-save", ex);
        }
    }
}

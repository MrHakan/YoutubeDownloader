using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Channels;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Core.Resolving;
using YoutubeDownloader.Core.Utils;
using YoutubeDownloader.Services;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos.Streams;
using AppProgram = YoutubeDownloader.Program;

var failures = 0;
var total = 0;
async Task Test(string name, Func<Task> run)
{
    total++;
    try
    {
        await run();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine("FAIL " + name + ": " + ex);
    }
}
Task Sync(Action run)
{
    run();
    return Task.CompletedTask;
}
void Assert(bool condition, string message = "Assertion failed")
{
    if (!condition)
        throw new Exception(message);
}
async Task Expect<T>(Func<Task> run)
    where T : Exception
{
    try
    {
        await run();
    }
    catch (T)
    {
        return;
    }
    throw new Exception("Expected " + typeof(T).Name);
}
async Task Wait(Func<bool> condition)
{
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    while (!condition())
        await Task.Delay(10, limit.Token);
}
DownloadRequest Request(TestDirectory dir, string file = "video.mp4") =>
    new()
    {
        VideoId = "jNQXAC9IVRw",
        Title = "Test",
        FilePath = dir.File(file),
    };
DownloadQueue Queue(TestDirectory dir, IDownloadExecutor executor) =>
    new(executor, dir.File("queue.json"), new(dir.File("diagnostic.log"))) { Limit = 1 };

await Test(
    "failed output preserves original and cleans only its temporary files",
    () =>
        Sync(() =>
        {
            using var dir = new TestDirectory();
            var path = dir.File("old.mp4");
            File.WriteAllText(path, "original");
            File.WriteAllText(dir.File("unrelated.tmp"), "keep");
            using (var transaction = new SafeOutputFile(path, true))
                File.WriteAllText(transaction.TemporaryPath, "partial");
            Assert(File.ReadAllText(path) == "original");
            Assert(File.Exists(dir.File("unrelated.tmp")));
            Assert(!Directory.EnumerateDirectories(dir.Path).Any());
        })
);
await Test(
    "interrupted temporary files are recovered only with matching ownership",
    async () =>
    {
        using var dir = new TestDirectory();
        var path = dir.File("video.mp4");
        var id = Guid.NewGuid();
        var abandoned = new SafeOutputFile(path, false, id);
        File.WriteAllText(abandoned.TemporaryPath, "partial");
        using (var recovered = new SafeOutputFile(path, false, id))
        {
            Assert(!File.Exists(recovered.TemporaryPath));
            File.WriteAllText(recovered.TemporaryPath, "complete");
            recovered.Commit();
        }
        abandoned.Dispose();
        Assert(File.ReadAllText(path) == "complete");
        var foreign = Guid.NewGuid();
        var folder = dir.File($".yd-{foreign:N}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, "keep"), "user data");
        await Expect<IOException>(() =>
            Sync(() =>
            {
                using var tx = new SafeOutputFile(path, true, foreign);
            })
        );
        Assert(File.Exists(System.IO.Path.Combine(folder, "keep")));
    }
);
await Test(
    "successful overwrite replaces original",
    () =>
        Sync(() =>
        {
            using var dir = new TestDirectory();
            var path = dir.File("old.mp4");
            File.WriteAllText(path, "original");
            using (var transaction = new SafeOutputFile(path, true))
            {
                File.WriteAllText(transaction.TemporaryPath, "new media");
                transaction.Commit();
            }
            Assert(File.ReadAllText(path) == "new media");
        })
);
await Test(
    "unapproved overwrite and empty output preserve destination",
    async () =>
    {
        using var dir = new TestDirectory();
        var path = dir.File("old.mp4");
        File.WriteAllText(path, "original");
        using (var tx = new SafeOutputFile(path, false))
        {
            File.WriteAllText(tx.TemporaryPath, "new");
            await Expect<IOException>(() => Sync(tx.Commit));
        }
        using (var tx = new SafeOutputFile(path, true))
        {
            File.WriteAllText(tx.TemporaryPath, "");
            await Expect<IOException>(() => Sync(tx.Commit));
        }
        Assert(File.ReadAllText(path) == "original");
    }
);
await Test(
    "failed commit preserves destination when replacement is impossible",
    async () =>
    {
        using var dir = new TestDirectory();
        var path = dir.File("destination.mp4");
        Directory.CreateDirectory(path);
        using var tx = new SafeOutputFile(path, true);
        File.WriteAllText(tx.TemporaryPath, "new");
        await Expect<IOException>(() => Sync(tx.Commit));
        Assert(Directory.Exists(path));
    }
);
await Test(
    "retry refreshes each attempt and succeeds within three",
    async () =>
    {
        var attempts = 0;
        var result = await DownloadFailure.RetryAsync<int>(
            (_, _) =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException<int>(new HttpRequestException("network"))
                    : Task.FromResult(42);
            },
            CancellationToken.None,
            delay: (_, _) => Task.CompletedTask
        );
        Assert(result == 42 && attempts == 3);
    }
);
await Test(
    "retry stops at three and permanent failures are not retried",
    async () =>
    {
        var attempts = 0;
        await Expect<HttpRequestException>(() =>
            DownloadFailure.RetryAsync<int>(
                (_, _) =>
                {
                    attempts++;
                    throw new HttpRequestException("network");
                },
                CancellationToken.None,
                delay: (_, _) => Task.CompletedTask
            )
        );
        Assert(attempts == 3);
        attempts = 0;
        await Expect<IOException>(() =>
            DownloadFailure.RetryAsync<int>(
                (_, _) =>
                {
                    attempts++;
                    throw new IOException("disk");
                },
                CancellationToken.None
            )
        );
        Assert(attempts == 1);
        Assert(
            !DownloadFailure.CanRetry(
                new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized),
                CancellationToken.None
            )
        );
    }
);
await Test(
    "canceling retry delay prevents new attempts",
    async () =>
    {
        using var canceled = new CancellationTokenSource();
        var attempts = 0;
        await Expect<OperationCanceledException>(() =>
            DownloadFailure.RetryAsync<int>(
                (_, _) =>
                {
                    attempts++;
                    throw new HttpRequestException("network");
                },
                canceled.Token,
                onRetry: _ => canceled.Cancel()
            )
        );
        Assert(attempts == 1);
    }
);
await Test(
    "queue reserves unique paths without creating output files",
    async () =>
    {
        using var dir = new TestDirectory();
        var executor = new ControlledExecutor();
        var queue = Queue(dir, executor);
        var one = queue.Enqueue(Request(dir));
        await executor.Started.Reader.ReadAsync();
        var two = queue.Enqueue(Request(dir) with { VideoId = "abcdefghijk" });
        Assert(one.Request.FilePath != two.Request.FilePath);
        Assert(!File.Exists(one.Request.FilePath) && !File.Exists(two.Request.FilePath));
        await queue.ShutdownAsync();
    }
);
await Test(
    "duplicate pending job is reused",
    async () =>
    {
        using var dir = new TestDirectory();
        var executor = new ControlledExecutor();
        var queue = Queue(dir, executor);
        var request = Request(dir);
        var first = queue.Enqueue(request);
        var second = queue.Enqueue(request with { Id = Guid.NewGuid() });
        Assert(ReferenceEquals(first, second) && queue.Jobs.Count == 1);
        await queue.ShutdownAsync();
    }
);
await Test(
    "FIFO, reprioritization and queued cancellation",
    async () =>
    {
        using var dir = new TestDirectory();
        var executor = new ControlledExecutor();
        var queue = Queue(dir, executor);
        var first = queue.Enqueue(Request(dir, "a.mp4"));
        await executor.Started.Reader.ReadAsync();
        var second = queue.Enqueue(Request(dir, "b.mp4"));
        var third = queue.Enqueue(Request(dir, "c.mp4"));
        queue.Move(third.Request.Id, -1);
        queue.Cancel(second.Request.Id);
        executor.Release.Release();
        var next = await executor
            .Started.Reader.ReadAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(8));
        Assert(next.Id == third.Request.Id && second.Status == QueueStatus.Canceled);
        executor.Release.Release();
        await Wait(() => third.Status == QueueStatus.Completed);
        await queue.ShutdownAsync();
    }
);
await Test(
    "changing concurrency starts pending work; cancel frees a slot",
    async () =>
    {
        using var dir = new TestDirectory();
        var executor = new ControlledExecutor();
        var queue = Queue(dir, executor);
        var first = queue.Enqueue(Request(dir, "a.mp4"));
        await executor.Started.Reader.ReadAsync();
        var second = queue.Enqueue(Request(dir, "b.mp4"));
        queue.Limit = 2;
        Assert(
            (
                await executor
                    .Started.Reader.ReadAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(8))
            ).Id == second.Request.Id
        );
        queue.Cancel(first.Request.Id);
        await Wait(() => first.Status == QueueStatus.Canceled);
        await queue.ShutdownAsync();
    }
);
await Test(
    "shutdown persists unfinished jobs; restore waits for explicit restart",
    async () =>
    {
        using var dir = new TestDirectory();
        var executor = new ControlledExecutor();
        var queue = Queue(dir, executor);
        queue.Enqueue(Request(dir, "a.mp4"));
        queue.Enqueue(Request(dir, "b.mp4"));
        await executor.Started.Reader.ReadAsync();
        await queue.ShutdownAsync();
        var nextExecutor = new ControlledExecutor();
        var restored = Queue(dir, nextExecutor);
        restored.Load();
        Assert(
            restored.Jobs.Count == 2 && restored.Jobs.All(j => j.Status == QueueStatus.Interrupted)
        );
        Assert(!nextExecutor.Started.Reader.TryRead(out _));
        restored.Restart(restored.Jobs[0].Request.Id);
        await nextExecutor.Started.Reader.ReadAsync();
        await restored.ShutdownAsync();
    }
);
await Test(
    "failed job does not block the next job",
    async () =>
    {
        using var dir = new TestDirectory();
        var executor = new ControlledExecutor { FailFirst = true };
        var queue = Queue(dir, executor);
        var first = queue.Enqueue(Request(dir, "a.mp4"));
        var second = queue.Enqueue(Request(dir, "b.mp4"));
        await executor.Started.Reader.ReadAsync();
        await executor.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8));
        Assert(first.Status == QueueStatus.Failed);
        executor.Release.Release();
        await Wait(() => second.Status == QueueStatus.Completed);
        await queue.ShutdownAsync();
    }
);
await Test(
    "corrupt queue is preserved and does not crash startup",
    () =>
        Sync(() =>
        {
            using var dir = new TestDirectory();
            File.WriteAllText(dir.File("queue.json"), "{broken");
            var queue = Queue(dir, new ControlledExecutor());
            queue.Load();
            Assert(
                queue.StorageError is not null
                    && File.ReadAllText(dir.File("queue.json.unreadable")) == "{broken"
            );
            File.WriteAllText(dir.File("queue.json"), "{\"Version\":1,\"Jobs\":null}");
            queue.Load();
            Assert(queue.StorageError is not null);
        })
);
await Test(
    "format preferences select video, not MP4 audio",
    () =>
        Sync(() =>
        {
            VideoDownloadOption Option(int height) =>
                new(
                    Container.Mp4,
                    false,
                    [
                        new VideoOnlyStreamInfo(
                            "https://test.invalid",
                            Container.Mp4,
                            new FileSize(100),
                            new Bitrate(100),
                            "avc1",
                            new VideoQuality(height, 30),
                            new Resolution(height * 16 / 9, height)
                        ),
                    ]
                );
            var audio = new VideoDownloadOption(
                Container.Mp4,
                true,
                [
                    new AudioOnlyStreamInfo(
                        "https://test.invalid",
                        Container.Mp4,
                        new FileSize(100),
                        new Bitrate(100),
                        "aac",
                        null,
                        null
                    ),
                ]
            );
            var low = Option(360);
            var high = Option(1080);
            VideoDownloadOption[] options = [audio, high, low];
            Assert(
                new VideoDownloadPreference(
                    Container.Mp4,
                    VideoQualityPreference.Lowest
                ).TryGetBestOption(options) == low
            );
            Assert(
                new VideoDownloadPreference(
                    Container.Mp4,
                    VideoQualityPreference.Highest
                ).TryGetBestOption(options) == high
            );
            Assert(
                new VideoDownloadPreference(
                    Container.Mp4,
                    VideoQualityPreference.UpTo720p
                ).TryGetBestOption(options) == low
            );
            Assert(
                new VideoDownloadPreference(
                    Container.Mp4,
                    VideoQualityPreference.Highest
                ).TryGetBestOption([audio])
                    is null
            );
            Assert(
                new VideoDownloadPreference(
                    Container.Mp4,
                    VideoQualityPreference.UpTo360p
                ).TryGetBestOption([high])
                    is null
            );
        })
);
await Test(
    "file names expand tokens and sanitize separators",
    () =>
        Sync(() =>
        {
            var video = new PlaylistVideo(
                "PL0000000000000000",
                "jNQXAC9IVRw",
                "a/b\\c",
                new Author("UC0v-tlzsn0QZwJnkiaUSJVQ", "author"),
                TimeSpan.FromSeconds(19),
                []
            );
            var name = FileNameTemplate.Apply(
                "$num $title $id $author",
                video,
                Container.Mp4,
                "01"
            );
            Assert(
                name.EndsWith(".mp4")
                    && name.Contains("[01]")
                    && name.Contains("jNQXAC9IVRw")
                    && !name.Contains('/')
                    && !name.Contains('\\')
            );
        })
);
await Test(
    "settings and profiles round-trip; updater is off by default",
    () =>
        Sync(() =>
        {
            using var dir = new TestDirectory();
            var settings = new SettingsService(dir.Path);
            Assert(!settings.IsAutoUpdateEnabled);
            settings.Language = "en";
            settings.LastDownloadDirectory = dir.File("media");
            settings.Profiles =
            [
                new("custom", "My profile", "mp3", VideoQualityPreference.Highest),
            ];
            settings.SelectedProfileId = "custom";
            settings.Save();
            var loaded = new SettingsService(dir.Path);
            Assert(loaded.Load());
            Assert(
                loaded.Language == "en"
                    && loaded.Profiles[0].Name == "My profile"
                    && loaded.LastDownloadDirectory == settings.LastDownloadDirectory
            );
        })
);
await Test(
    "corrupt settings do not crash",
    () =>
        Sync(() =>
        {
            using var dir = new TestDirectory();
            File.WriteAllText(dir.File("Settings.dat"), "{broken");
            var settings = new SettingsService(dir.Path);
            Assert(
                !settings.Load()
                    && settings.PersistenceError is not null
                    && File.Exists(dir.File("Settings.dat.unreadable"))
            );
        })
);
await Test(
    "legacy settings migration resets upstream update opt-in",
    () =>
        Sync(() =>
        {
            using var dir = new TestDirectory();
            var legacy = dir.File("legacy.dat");
            File.WriteAllText(
                legacy,
                "{\"IsAutoUpdateEnabled\":true,\"ParallelLimit\":4,\"LastAuthCookies\":null}"
            );
            var settings = new SettingsService(dir.File("data"), legacy);
            Assert(settings.Load());
            Assert(!settings.IsAutoUpdateEnabled && settings.ParallelLimit == 4);
            Assert(File.Exists(legacy + ".migration-backup"));
        })
);
await Test(
    "log does not record exception secrets and rotates",
    () =>
        Sync(() =>
        {
            using var dir = new TestDirectory();
            var path = dir.File("diagnostic.log");
            var log = new DiagnosticLog(path);
            log.Write(
                "operation https://example.invalid?signature=secret",
                new Exception("Cookie: MY_SECRET_TOKEN")
            );
            Assert(!File.ReadAllText(path).Contains("secret", StringComparison.OrdinalIgnoreCase));
            Assert(
                !DiagnosticLog
                    .Redact("Authorization: MY_SECRET\nCookie=OTHER_SECRET")
                    .Contains("SECRET")
            );
            File.WriteAllText(path, new string('x', 600000));
            log.Write("next");
            Assert(File.Exists(path + ".previous"));
        })
);
await Test(
    "optional content failure warns, but cancellation propagates",
    async () =>
    {
        var warnings = new List<string>();
        await OptionalContent.TryAsync(
            () => throw new HttpRequestException("captions"),
            "SubtitlesUnavailable",
            warnings.Add,
            CancellationToken.None
        );
        Assert(warnings.SequenceEqual(new[] { "SubtitlesUnavailable" }));
        await Expect<OperationCanceledException>(() =>
            OptionalContent.TryAsync(
                () => throw new OperationCanceledException(),
                "TagsUnavailable",
                warnings.Add,
                CancellationToken.None
            )
        );
        Assert(warnings.Count == 1);
    }
);
await Test(
    "bare video IDs are disambiguated from playlists",
    () =>
        Sync(() =>
        {
            Assert(QueryResolver.IsBareVideoId("jNQXAC9IVRw"));
            Assert(!QueryResolver.IsBareVideoId("WL"));
            Assert(
                !QueryResolver.IsBareVideoId(
                    "https://www.youtube.com/watch?v=jNQXAC9IVRw&list=PL1234"
                )
            );
        })
);
await Test(
    "custom update route and disabled check",
    async () =>
    {
        using var dir = new TestDirectory();
        var settings = new SettingsService(dir.Path);
        using var updates = new UpdateService(settings);
        Assert(AppProgram.ProjectUrl == "https://github.com/MrHakan/YoutubeDownloader");
        Assert(UpdateService.GetAssetName("win-x64", true) == "YoutubeDownloader.win-x64.zip");
        Assert(
            UpdateService.GetAssetName("linux-x64", false) == "YoutubeDownloader.Bare.linux-x64.zip"
        );
        Assert(await updates.CheckForUpdatesAsync() is null);
    }
);
await Test(
    "localization has both languages for the main flow",
    () =>
        Sync(() =>
        {
            Localization.Apply("tr");
            Assert(Localization.Get("Settings") == "Ayarlar");
            Assert(Localization.Get("Downloading") == "İndiriliyor");
            Localization.Apply("en");
            Assert(Localization.Get("Settings") == "Settings");
            Assert(Localization.Get("ErrorNetwork").StartsWith("Connection"));
            Localization.Apply("tr");
        })
);
if (OperatingSystem.IsWindows())
{
    await Test(
        "authentication is encrypted and excluded from settings",
        () =>
            Sync(() =>
            {
                using var dir = new TestDirectory();
                var settings = new SettingsService(dir.Path);
                settings.LastAuthCookies =
                [
                    new Cookie("test", "AUTH_SECRET_SENTINEL", "/", ".youtube.com")
                    {
                        Expires = DateTime.UtcNow.AddDays(1),
                    },
                ];
                settings.Save();
                Assert(settings.PersistenceError is null);
                Assert(!File.ReadAllText(dir.File("Settings.dat")).Contains("AUTH_SECRET"));
                Assert(
                    !System
                        .Text.Encoding.UTF8.GetString(File.ReadAllBytes(dir.File("auth.protected")))
                        .Contains("AUTH_SECRET")
                );
                var loaded = new SettingsService(dir.Path);
                Assert(
                    loaded.Load()
                        && loaded.LastAuthCookies?.Single().Value == "AUTH_SECRET_SENTINEL"
                );
                loaded.IsAuthPersisted = false;
                loaded.Save();
                Assert(!File.Exists(dir.File("auth.protected")));
            })
    );
}

await Test(
    "playlist setup uses the selected shared profile",
    () =>
        Sync(() =>
        {
            using var dir = new TestDirectory();
            var settings = new SettingsService(dir.Path) { SelectedProfileId = "mp3" };
            using var setup =
                new YoutubeDownloader.ViewModels.Dialogs.DownloadMultipleSetupViewModel(
                    null!,
                    null!,
                    settings
                );
            setup.InitializeCommand.Execute(null);
            Assert(
                setup.SelectedContainer == Container.Mp3
                    && setup.SelectedVideoQualityPreference == VideoQualityPreference.Highest
            );
            settings.SelectedProfileId = "720";
            setup.InitializeCommand.Execute(null);
            Assert(
                setup.SelectedContainer == Container.Mp4
                    && setup.SelectedVideoQualityPreference == VideoQualityPreference.UpTo720p
            );
        })
);

if (args.Contains("--ui"))
    await Test("UI initialization, dialogs and both languages", () => Sync(UiSmoke.Run));
if (args.Contains("--live"))
{
    await Test(
        "LIVE public playlist resolution",
        async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var resolver = new QueryResolver();
            const string playlistId = "PLI5YfMzCfRtZ8eV576YoY3vIYrHjyVm_e";
            var result = await resolver.ResolveAsync(
                "https://www.youtube.com/playlist?list=" + playlistId,
                timeout.Token
            );
            Assert(result.Kind == QueryResultKind.Playlist && result.Videos.Count > 0);
            Console.WriteLine($"LIVE playlist={playlistId} resolved {result.Videos.Count} videos");
        }
    );
    await Test(
        "LIVE fresh MP4 + MP3 downloads and cancellation",
        async () =>
        {
            using var dir = new TestDirectory();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var settings = new SettingsService(dir.Path);
            var executor = new YoutubeDownloadExecutor(settings);
            Console.WriteLine(
                $"LIVE video=jNQXAC9IVRw library={typeof(YoutubeClient).Assembly.GetName().Version}"
            );
            foreach (var format in new[] { "mp4", "mp3" })
            {
                var path = dir.File("media." + format);
                var stages = new HashSet<DownloadStage>();
                await executor.ExecuteAsync(
                    Request(dir) with
                    {
                        FilePath = path,
                        Container = format,
                        AudioOnly = format == "mp3",
                        Quality = VideoQualityPreference.Lowest,
                    },
                    new CallbackProgress<TransferProgress>(p => stages.Add(p.Stage)),
                    timeout.Token
                );
                Assert(
                    new FileInfo(path).Length > 1000 && stages.Contains(DownloadStage.Converting)
                );
                var start = new System.Diagnostics.ProcessStartInfo(FFmpeg.TryGetCliFilePath()!)
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                foreach (var part in new[] { "-v", "error", "-i", path, "-f", "null", "-" })
                    start.ArgumentList.Add(part);
                using var process = System.Diagnostics.Process.Start(start)!;
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(timeout.Token);
                Assert(process.ExitCode == 0, await error);
                Console.WriteLine($"LIVE {format}: decoded {new FileInfo(path).Length} bytes");
            }
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            await Expect<OperationCanceledException>(() =>
                executor.ExecuteAsync(
                    Request(dir),
                    new CallbackProgress<TransferProgress>(_ => { }),
                    cancel.Token
                )
            );
            var existing = dir.File("preserved.mp4");
            File.WriteAllText(existing, "previous file");
            using var transferCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token
            );
            await Expect<OperationCanceledException>(() =>
                executor.ExecuteAsync(
                    Request(dir) with
                    {
                        FilePath = existing,
                        Quality = VideoQualityPreference.Lowest,
                        Overwrite = true,
                    },
                    new CallbackProgress<TransferProgress>(p =>
                    {
                        if (p.Stage == DownloadStage.Downloading)
                            transferCancellation.Cancel();
                    }),
                    transferCancellation.Token
                )
            );
            Assert(File.ReadAllText(existing) == "previous file");
            Assert(!Directory.EnumerateDirectories(dir.Path, ".yd-*").Any());
            Console.WriteLine(
                "LIVE canceled during transfer: original file preserved, temporary files removed"
            );
        }
    );
}
Console.WriteLine($"{total - failures}/{total} tests passed.");
return failures == 0 ? 0 : 1;

sealed class TestDirectory : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(AppContext.BaseDirectory, "test-work", Guid.NewGuid().ToString("N"));

    public TestDirectory() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose() => Directory.Delete(Path, true);
}

sealed class ControlledExecutor : IDownloadExecutor
{
    public Channel<DownloadRequest> Started { get; } = Channel.CreateUnbounded<DownloadRequest>();
    public SemaphoreSlim Release { get; } = new(0);
    public bool FailFirst { get; init; }
    private int _count;

    public async Task<DownloadExecutionResult> ExecuteAsync(
        DownloadRequest request,
        IProgress<TransferProgress> progress,
        CancellationToken token
    )
    {
        await Started.Writer.WriteAsync(request, token);
        if (Interlocked.Increment(ref _count) == 1 && FailFirst)
            throw new IOException("simulated disk failure");
        await Release.WaitAsync(token);
        progress.Report(new(DownloadStage.Converting, 1));
        return new();
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Gress;
using YoutubeDownloader.Core.Tagging;
using YoutubeDownloader.Core.Utils;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.ClosedCaptions;

namespace YoutubeDownloader.Core.Downloading;

public class VideoDownloader(IReadOnlyList<Cookie>? initialCookies = null) : IDisposable
{
    private readonly YoutubeClient _youtube = new(Http.Client, initialCookies ?? []);

    public async Task<IReadOnlyList<VideoDownloadOption>> GetDownloadOptionsAsync(
        VideoId videoId,
        bool includeLanguageSpecificAudioStreams = true,
        CancellationToken cancellationToken = default
    )
    {
        var manifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken);
        return VideoDownloadOption.ResolveAll(manifest, includeLanguageSpecificAudioStreams);
    }

    public async Task<VideoDownloadOption> GetBestDownloadOptionAsync(
        VideoId videoId,
        VideoDownloadPreference preference,
        bool includeLanguageSpecificAudioStreams = true,
        CancellationToken cancellationToken = default
    )
    {
        var options = await GetDownloadOptionsAsync(
            videoId,
            includeLanguageSpecificAudioStreams,
            cancellationToken
        );

        return preference.TryGetBestOption(options)
            ?? throw new InvalidOperationException("No suitable download option found.");
    }

    public async Task DownloadVideoAsync(
        string filePath,
        IVideo video,
        VideoDownloadOption downloadOption,
        bool includeSubtitles = true,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default,
        bool overwrite = false,
        bool includeTags = false,
        Action<string>? warning = null,
        IProgress<TransferProgress>? details = null,
        Guid? operationId = null
    )
    {
        using var output = new SafeOutputFile(filePath, overwrite, operationId);
        var ffmpeg =
            FFmpeg.TryGetCliFilePath() ?? throw new FileNotFoundException("FFmpeg is missing.");
        // Include subtitles in the output container
        var trackInfos = new List<ClosedCaptionTrackInfo>();
        if (includeSubtitles && !downloadOption.IsAudioOnly)
        {
            await OptionalContent.TryAsync(
                async () =>
                {
                    var manifest = await _youtube.Videos.ClosedCaptions.GetManifestAsync(
                        video.Id,
                        cancellationToken
                    );
                    trackInfos.AddRange(manifest.Tracks);
                },
                "SubtitlesUnavailable",
                warning,
                cancellationToken
            );
        }

        var dirPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dirPath))
            Directory.CreateDirectory(dirPath);

        var conversionWeight =
            0.05
            + 5 * downloadOption.StreamInfos.Count(s => s.Container != downloadOption.Container);
        var totalWeight = 1.01 + conversionWeight;
        var streamEnd = 1 / totalWeight;
        var subtitleEnd = 1.01 / totalWeight;
        var clock = Stopwatch.StartNew();
        var totalBytes = downloadOption.StreamInfos.Sum(s => (double)s.Size.Bytes);
        var lastStage = DownloadStage.Downloading;
        var report = new CallbackProgress<double>(p =>
        {
            progress?.Report(Percentage.FromFraction(Math.Clamp(p, 0, 1)));
            lastStage =
                p < streamEnd ? DownloadStage.Downloading
                : p < subtitleEnd ? DownloadStage.Subtitles
                : DownloadStage.Converting;
            var transferFraction = Math.Clamp(p / streamEnd, 0, 1);
            var speed =
                clock.Elapsed.TotalSeconds > 0.5
                    ? totalBytes * transferFraction / clock.Elapsed.TotalSeconds
                    : 0;
            details?.Report(
                new(
                    lastStage,
                    p,
                    lastStage == DownloadStage.Downloading && speed > 0 ? speed : null,
                    lastStage == DownloadStage.Downloading && speed > 0
                        ? TimeSpan.FromSeconds(
                            Math.Min(86400, totalBytes * (1 - transferFraction) / speed)
                        )
                        : null
                )
            );
        });
        async Task ConvertAsync(IReadOnlyList<ClosedCaptionTrackInfo> captions) =>
            await _youtube.Videos.DownloadAsync(
                downloadOption.StreamInfos,
                captions,
                new ConversionRequestBuilder(output.TemporaryPath)
                    .SetFFmpegPath(ffmpeg)
                    .SetContainer(downloadOption.Container)
                    .SetPreset(ConversionPreset.Medium)
                    .Build(),
                report,
                cancellationToken
            );
        try
        {
            await ConvertAsync(trackInfos);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException
                && !cancellationToken.IsCancellationRequested
                && trackInfos.Count > 0
                && lastStage == DownloadStage.Subtitles
            )
        {
            warning?.Invoke("SubtitlesUnavailable");
            clock.Restart();
            await ConvertAsync([]);
        }
        if (includeTags)
        {
            details?.Report(new(DownloadStage.Tagging, 1));
            await OptionalContent.TryAsync(
                async () =>
                {
                    await new MediaTagInjector().InjectTagsAsync(
                        output.TemporaryPath,
                        video,
                        cancellationToken
                    );
                },
                "TagsUnavailable",
                warning,
                cancellationToken
            );
        }
        cancellationToken.ThrowIfCancellationRequested();
        output.Commit();
    }

    public void Dispose() => _youtube.Dispose();
}

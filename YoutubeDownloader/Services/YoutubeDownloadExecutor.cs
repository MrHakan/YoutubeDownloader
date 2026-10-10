using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Core.Resolving;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.Services;

public sealed class YoutubeDownloadExecutor(SettingsService settings) : IDownloadExecutor
{
    public Task<DownloadExecutionResult> ExecuteAsync(
        DownloadRequest request,
        IProgress<TransferProgress> progress,
        CancellationToken token
    ) =>
        DownloadFailure.RetryAsync(
            async (_, ct) =>
            {
                progress.Report(new(DownloadStage.Resolving));
                using var resolver = new QueryResolver(settings.LastAuthCookies);
                var result = await resolver.ResolveAsync(
                    "https://www.youtube.com/watch?v=" + request.VideoId,
                    ct
                );
                var video = result.Videos.Single();
                using var downloader = new VideoDownloader(settings.LastAuthCookies);
                var options = await downloader.GetDownloadOptionsAsync(
                    video.Id,
                    request.IncludeLanguages,
                    ct
                );
                var candidates = options
                    .Where(o =>
                        o.Container.Name == request.Container && o.IsAudioOnly == request.AudioOnly
                    )
                    .ToArray();
                var option = request.ExactQuality is not null
                    ? candidates.FirstOrDefault(o => o.VideoQuality?.Label == request.ExactQuality)
                    : new VideoDownloadPreference(
                        new Container(request.Container),
                        request.Quality
                    ).TryGetBestOption(candidates);
                if (request.AudioOnly && request.ExactQuality is null)
                    option = candidates.FirstOrDefault();
                if (option is null)
                    throw new InvalidOperationException(
                        "Selected format or quality is unavailable."
                    );
                if (request.AudioLanguages.Length > 0)
                {
                    var streams = option
                        .StreamInfos.Where(s =>
                            s is not IAudioStreamInfo audio
                            || audio.AudioLanguage is null
                            || request.AudioLanguages.Contains(audio.AudioLanguage.Value.Code)
                        )
                        .ToArray();
                    if (!streams.OfType<IAudioStreamInfo>().Any())
                        throw new InvalidOperationException(
                            "Selected audio language is unavailable."
                        );
                    option = new(option.Container, option.IsAudioOnly, streams);
                }
                var warnings = new List<string>();
                await downloader.DownloadVideoAsync(
                    request.FilePath,
                    video,
                    option,
                    request.IncludeSubtitles,
                    cancellationToken: ct,
                    overwrite: request.Overwrite,
                    includeTags: request.IncludeTags,
                    warning: warnings.Add,
                    details: progress,
                    operationId: request.Id
                );
                return new DownloadExecutionResult(video, warnings.Distinct().ToArray());
            },
            token,
            attempt => progress.Report(new(DownloadStage.Retrying, attempt))
        );
}

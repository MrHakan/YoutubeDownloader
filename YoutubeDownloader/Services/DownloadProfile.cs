using YoutubeDownloader.Core.Downloading;

namespace YoutubeDownloader.Services;

public sealed record DownloadProfile(
    string Id,
    string Name,
    string Container,
    VideoQualityPreference Quality
)
{
    public override string ToString() => Localization.Get(Name);

    public static DownloadProfile[] Defaults =>
        [
            new("best", "Best video", "mp4", VideoQualityPreference.Highest),
            new("1080", "1080p", "mp4", VideoQualityPreference.UpTo1080p),
            new("720", "720p", "mp4", VideoQualityPreference.UpTo720p),
            new("mp3", "MP3", "mp3", VideoQualityPreference.Highest),
        ];
}

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode.Exceptions;

namespace YoutubeDownloader.Core.Downloading;

public enum DownloadFailureKind
{
    Network,
    Access,
    Format,
    FileSystem,
    Conversion,
    Unknown,
}

public static class DownloadFailure
{
    public static DownloadFailureKind Classify(Exception error) =>
        error switch
        {
            VideoUnavailableException or PlaylistUnavailableException => DownloadFailureKind.Access,
            UnauthorizedAccessException or IOException => DownloadFailureKind.FileSystem,
            HttpRequestException
            {
                StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            } => DownloadFailureKind.Access,
            HttpRequestException or TimeoutException or OperationCanceledException =>
                DownloadFailureKind.Network,
            InvalidOperationException => DownloadFailureKind.Format,
            _ when error.GetType().FullName?.StartsWith("CliWrap.", StringComparison.Ordinal)
                    == true => DownloadFailureKind.Conversion,
            _ => DownloadFailureKind.Unknown,
        };

    public static bool CanRetry(Exception error, CancellationToken token) =>
        !token.IsCancellationRequested
        && error switch
        {
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException
            {
                StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            } => true,
            HttpRequestException { StatusCode: { } status } => (int)status >= 500
                || status == HttpStatusCode.Forbidden,
            TimeoutException => true,
            OperationCanceledException => true,
            _ => false,
        };

    public static async Task<T> RetryAsync<T>(
        Func<int, CancellationToken, Task<T>> action,
        CancellationToken token,
        Action<int>? onRetry = null,
        Func<int, CancellationToken, Task>? delay = null
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return await action(attempt, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < 3 && CanRetry(ex, token))
            {
                onRetry?.Invoke(attempt + 1);
                await (
                    delay?.Invoke(attempt, token)
                    ?? Task.Delay(TimeSpan.FromSeconds(2 * attempt), token)
                ).ConfigureAwait(false);
            }
        }
    }
}

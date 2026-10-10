using System;
using System.Threading;
using System.Threading.Tasks;

namespace YoutubeDownloader.Core.Downloading;

public static class OptionalContent
{
    public static async Task TryAsync(
        Func<Task> action,
        string warningCode,
        Action<string>? warning,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            warning?.Invoke(warningCode);
        }
        token.ThrowIfCancellationRequested();
    }
}

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace YoutubeDownloader.Core.Utils.Extensions;

public static class AsyncCollectionExtensions
{
    public static ValueTaskAwaiter<IReadOnlyList<T>> GetAwaiter<T>(
        this IAsyncEnumerable<T> asyncEnumerable
    )
    {
        async ValueTask<IReadOnlyList<T>> CollectAsync()
        {
            var list = new List<T>();

            await foreach (var i in asyncEnumerable)
                list.Add(i);

            return list;
        }

        return CollectAsync().GetAwaiter();
    }
}

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace YoutubeDownloader.Core.Utils;

public sealed class DiagnosticLog(string path)
{
    private readonly object _gate = new();

    public static string Redact(string text)
    {
        text = Regex.Replace(text, @"https?://[^\s]+", "[url]", RegexOptions.IgnoreCase);
        return Regex.Replace(
            text,
            @"(?i)(cookie|authorization|token|signature|password)\s*[:=]\s*[^\r\n]+",
            "$1=[redacted]"
        );
    }

    public void Write(string operation, Exception? error = null)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                    File.Move(path, path + ".previous", true);
                // Exception messages can contain credentials and signed URLs. Record type and stack only.
                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.UtcNow:O} {Redact(operation)} {error?.GetType().Name}\n{Redact(error?.StackTrace ?? "")}\n"
                );
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
